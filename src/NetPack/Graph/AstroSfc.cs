namespace NetPack.Graph;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
// `Node` is ambiguous in this namespace (NetPack.Graph.Node vs the AST node), so
// alias the AST node type, matching the convention already used in VueSfc.cs.
using AstNode = NetPack.Syntax.Ast.Node;

/// <summary>
/// Compiles an Astro-style <c>.astro</c> single-file component into a virtual
/// JavaScript module. A <c>.astro</c> file is a <c>---</c>-fenced "frontmatter"
/// module (imports, top-level statements, arbitrary JS/TS — the same grammar as
/// a plain module body) followed by a JSX-like template. The frontmatter's
/// imports are hoisted to the generated module's top level (so netpack resolves
/// them as ordinary dependencies, e.g. <c>import Layout from './Layout.astro'</c>);
/// everything else in the frontmatter is re-executed on every render, moved
/// verbatim into the body of the generated component function:
///
/// <code>
/// import Layout from "./Layout.astro";
///
/// export default async function render(props, slots) {
///   const title = "Hello";
///   // ...rest of the frontmatter...
///   return html`...`;
/// }
/// </code>
///
/// The template is parsed as JSX (reusing netpack's own JSX parser — this is
/// what gives correct, case-sensitive component-vs-host-element detection,
/// exactly like real JSX/Astro; an HTML parser would lowercase-normalize tag
/// names and lose that distinction) and lowered into a tagged template literal
/// that builds the final HTML string at call time, escaping plain interpolated
/// values and inlining already-rendered child-component output unescaped.
///
/// Known scope, deliberately not (yet) implemented:
/// <list type="bullet">
/// <item>No <c>client:*</c> hydration/islands — directives parse fine (JSX
/// already supports namespaced attribute names like <c>client:load</c>) but
/// carry no special runtime behavior; they're emitted as inert, literal HTML
/// attributes.</item>
/// <item>No <c>&lt;style&gt;</c>/<c>&lt;script&gt;</c> blocks inside the
/// template — they're stripped during compilation rather than given Vue-style
/// scoping/extraction.</item>
/// <item>Only the default slot — a component's children become
/// <c>slots.default</c>; there's no named-slot syntax yet.</item>
/// <item>No <c>Astro.*</c> global — <c>props</c>/<c>slots</c> are the
/// generated function's own parameters instead.</item>
/// <item>Void elements need an explicit self-close (<c>&lt;img /&gt;</c>), same
/// as JSX/React — this is a JSX parse, not a lenient HTML5 one.</item>
/// </list>
/// </summary>
public static class AstroSfc
{
    // Frontmatter statements and the template are both parsed with the same,
    // maximally permissive options: TypeScript (frontmatter may use it freely)
    // and JSX (needed to parse the template; harmless for the frontmatter).
    private static readonly ParserOptions SfcOptions = new() { Tolerant = true, TypeScript = true, Jsx = true };

    // `.astro` templates don't get their own <style>/<script> handling yet (see
    // the type doc above), and HTML comments aren't valid JSX — both are
    // stripped before the template is parsed, rather than risk a confusing
    // parse failure (a <style> block's own `{ ... }` rule bodies would
    // otherwise be misread as JSX expression containers).
    private static readonly Regex CommentPattern = new(@"<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex RawBlockPattern = new(@"<(style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>Compiles the full text of a <c>.astro</c> file into a virtual JS module.</summary>
    public static string Generate(string source, string fileName)
    {
        var (frontmatter, template) = SplitSections(source);
        var (imports, body) = SplitFrontmatter(frontmatter, fileName);
        var templateLiteral = CompileTemplate(template, fileName);

        var sb = new StringBuilder();
        sb.Append(Runtime);

        foreach (var import in imports)
        {
            sb.Append(import).Append('\n');
        }

        sb.Append("export default async function render(props, slots) {\n");
        sb.Append("  // frontmatter — re-executed on every render\n");
        sb.Append(body);
        sb.Append("  return html`").Append(templateLiteral).Append("`;\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Splits on the file's leading <c>---</c> fence pair. A file that doesn't
    /// open with one has no frontmatter (a template-only component).
    /// </summary>
    private static (string Frontmatter, string Template) SplitSections(string source)
    {
        var trimmed = source.TrimStart('\uFEFF');

        if (!trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return ("", source);
        }

        var afterOpenFence = trimmed.IndexOf('\n', 3);

        if (afterOpenFence < 0)
        {
            return ("", source);
        }

        var closeFence = trimmed.IndexOf("\n---", afterOpenFence, StringComparison.Ordinal);

        if (closeFence < 0)
        {
            return ("", source);
        }

        var frontmatter = trimmed[(afterOpenFence + 1)..closeFence];
        var afterCloseFence = trimmed.IndexOf('\n', closeFence + 1);
        var template = afterCloseFence < 0 ? "" : trimmed[(afterCloseFence + 1)..];
        return (frontmatter, template);
    }

    /// <summary>
    /// Parses the frontmatter as a module and splits its top-level statements
    /// into two groups: import/export declarations (which can only be valid at
    /// module scope, so they stay there — this is also how
    /// <c>import Layout from './Layout.astro'</c> gets resolved as an ordinary
    /// dependency by the rest of the bundler) and everything else, which is
    /// relocated verbatim into the generated <c>render</c> function's body so it
    /// re-runs on every call. Relative order is preserved within each group; a
    /// type-only import is dropped entirely (it has no runtime dependency).
    /// </summary>
    private static (List<string> Imports, string Body) SplitFrontmatter(string frontmatter, string fileName)
    {
        if (string.IsNullOrWhiteSpace(frontmatter))
        {
            return ([], "");
        }

        var module = Parser.ParseModule(frontmatter, fileName, SfcOptions);
        var imports = new List<string>();
        var body = new StringBuilder();

        foreach (var statement in module.Body)
        {
            switch (statement)
            {
                case EmptyStatement:
                case TypeOnlyDeclaration:
                    break;

                case ImportDeclaration { TypeOnly: true }:
                    break;

                case ImportDeclaration or ExportNamedDeclaration or ExportDefaultDeclaration or ExportAllDeclaration:
                    imports.Add(frontmatter[statement.Start..statement.End]);
                    break;

                default:
                    body.Append("  ").Append(frontmatter[statement.Start..statement.End]).Append('\n');
                    break;
            }
        }

        return (imports, body.ToString());
    }

    /// <summary>
    /// Parses the template as JSX (wrapped in a fragment so multiple root nodes
    /// are allowed, matching Astro) and lowers it into the text that goes
    /// between the backticks of the generated <c>html\`...\`</c> call.
    /// </summary>
    private static string CompileTemplate(string template, string fileName)
    {
        var cleaned = RawBlockPattern.Replace(CommentPattern.Replace(template, ""), "");
        var wrapped = "const __astroRoot = <>" + cleaned + "</>;\n";
        var module = Parser.ParseModule(wrapped, fileName, SfcOptions);

        if (module.Body.Count == 0
            || module.Body[0] is not VariableStatement variableStatement
            || variableStatement.Declarations.Count == 0
            || variableStatement.Declarations[0].Init is not { } root)
        {
            throw new InvalidOperationException($"Could not parse the template of '{fileName}' as JSX.");
        }

        var sb = new StringBuilder();

        switch (root)
        {
            case JsxFragment fragment:
                EmitChildren(fragment.Children, sb, wrapped);
                break;
            case JsxElement element:
                EmitElement(element, sb, wrapped);
                break;
            default:
                throw new InvalidOperationException($"The template of '{fileName}' is not valid JSX.");
        }

        return sb.ToString();
    }

    /// <summary>Emits a list of JSX children (text, <c>{expr}</c>, elements,
    /// nested fragments) as template-literal source text.</summary>
    private static void EmitChildren(IList<AstNode> children, StringBuilder sb, string source)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case JsxText text:
                    sb.Append(EscapeTemplateText(text.Value));
                    break;

                case JsxExpressionContainer { Expression: { } expr }:
                    // Pasted as real JS: the whole generated module is re-parsed
                    // afterwards, so this only needs to be syntactically an
                    // expression, not something this compiler understands.
                    sb.Append("${").Append(source[expr.Start..expr.End]).Append('}');
                    break;

                case JsxExpressionContainer:
                    // Empty `{}` — nothing to render.
                    break;

                case JsxElement element:
                    EmitElement(element, sb, source);
                    break;

                case JsxFragment fragment:
                    EmitChildren(fragment.Children, sb, source);
                    break;
            }
        }
    }

    /// <summary>
    /// Emits one JSX element. A capitalized (or dotted) tag name is a component
    /// reference — resolved exactly like any other default-imported netpack
    /// module, which for a compiled <c>.astro</c> file <i>is</i> its
    /// <c>render</c> function, so it's called directly, awaited, and its output
    /// (already an <see cref="AstNode"/>-free HTML string, wrapped so it isn't
    /// re-escaped) is interpolated in place. A lowercase tag name is emitted as
    /// a literal HTML element.
    /// </summary>
    private static void EmitElement(JsxElement element, StringBuilder sb, string source)
    {
        var (tagText, isComponent) = DescribeTagName(element.OpeningElement.Name, source);

        if (isComponent)
        {
            var propsText = BuildPropsObjectText(element.OpeningElement.Attributes, source);
            var childLiteral = new StringBuilder();
            EmitChildren(element.Children, childLiteral, source);
            sb.Append("${await (").Append(tagText).Append(")(").Append(propsText)
              .Append(", { default: html`").Append(childLiteral).Append("` })}");
            return;
        }

        sb.Append('<').Append(tagText);
        EmitAttributes(element.OpeningElement.Attributes, sb, source);

        if (element.OpeningElement.SelfClosing)
        {
            sb.Append(" />");
            return;
        }

        sb.Append('>');
        EmitChildren(element.Children, sb, source);
        sb.Append("</").Append(tagText).Append('>');
    }

    /// <summary>Emits a host element's attribute list as literal markup text
    /// (with <c>${...}</c> placeholders for dynamic values).</summary>
    private static void EmitAttributes(IList<AstNode> attributes, StringBuilder sb, string source)
    {
        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case JsxSpreadAttribute spread:
                    sb.Append("${__astroAttrs(").Append(source[spread.Argument.Start..spread.Argument.End]).Append(")}");
                    break;

                case JsxAttribute { Value: null } boolAttribute:
                    sb.Append(' ').Append(AttributeName(boolAttribute.Name));
                    break;

                case JsxAttribute { Value: StringLiteral literal } literalAttribute:
                    // A literal attribute value is author-controlled template
                    // text, same as plain JsxText — but it's spliced into a
                    // hardcoded `"..."` wrapper below, so it still needs
                    // HTML-escaping for correctness (a literal `"` in the value
                    // would otherwise break out of that wrapper).
                    sb.Append(' ').Append(AttributeName(literalAttribute.Name))
                      .Append("=\"").Append(EscapeTemplateText(HtmlEscape(literal.Value))).Append('"');
                    break;

                case JsxAttribute { Value: JsxExpressionContainer { Expression: { } expr } } exprAttribute:
                    sb.Append(' ').Append(AttributeName(exprAttribute.Name))
                      .Append("=\"${").Append(source[expr.Start..expr.End]).Append("}\"");
                    break;

                // A JsxElement attribute value or an empty `{}` isn't
                // meaningful as an HTML attribute — silently skipped.
            }
        }
    }

    /// <summary>Builds the <c>{ ... }</c> props object literal text passed to a
    /// component call from its JSX attributes.</summary>
    private static string BuildPropsObjectText(IList<AstNode> attributes, string source)
    {
        var parts = new List<string>();

        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case JsxSpreadAttribute spread:
                    parts.Add($"...({source[spread.Argument.Start..spread.Argument.End]})");
                    break;

                case JsxAttribute { Value: null } boolAttribute:
                    parts.Add($"{CssModules.JsString(PropKeyName(boolAttribute.Name))}: true");
                    break;

                case JsxAttribute { Value: StringLiteral literal } literalAttribute:
                    parts.Add($"{CssModules.JsString(PropKeyName(literalAttribute.Name))}: {CssModules.JsString(literal.Value)}");
                    break;

                case JsxAttribute { Value: JsxExpressionContainer { Expression: { } expr } } exprAttribute:
                    parts.Add($"{CssModules.JsString(PropKeyName(exprAttribute.Name))}: ({source[expr.Start..expr.End]})");
                    break;
            }
        }

        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>
    /// Resolves a JSX element name to its display text and whether it's a
    /// component reference — the same convention JSX/React itself uses: an
    /// identifier starting with an uppercase letter (or a dotted member
    /// expression, e.g. <c>Foo.Bar</c>) is a variable reference to call;
    /// anything else is a literal element name.
    /// </summary>
    private static (string Text, bool IsComponent) DescribeTagName(JsxName name, string source)
    {
        switch (name)
        {
            case JsxIdentifier identifier:
                var isComponent = identifier.Name.Length > 0 && char.IsUpper(identifier.Name[0]);
                return (identifier.Name, isComponent);

            case JsxMemberExpression:
                return (source[name.Start..name.End], true);

            case JsxNamespacedName ns:
                // e.g. `<svg:rect>` — there's no such thing as a "namespaced"
                // variable reference in JS, so this is always a literal name.
                return ($"{ns.Namespace.Name}:{ns.Name.Name}", false);

            default:
                return (source[name.Start..name.End], false);
        }
    }

    private static string AttributeName(JsxName name) => name switch
    {
        JsxIdentifier id => id.Name,
        JsxNamespacedName ns => $"{ns.Namespace.Name}:{ns.Name.Name}",
        _ => "data-attr",
    };

    private static string PropKeyName(JsxName name) => name switch
    {
        JsxIdentifier id => id.Name,
        JsxNamespacedName ns => $"{ns.Namespace.Name}:{ns.Name.Name}",
        _ => "prop",
    };

    /// <summary>Escapes text that's about to become a literal run inside the
    /// generated template literal (as opposed to HTML-escaping, which is a
    /// separate, runtime concern for dynamic values — see the <c>Runtime</c>
    /// helpers below).</summary>
    private static string EscapeTemplateText(string text)
        => text.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${");

    /// <summary>HTML-escapes a compile-time-known (literal) string, matching
    /// the rules <c>__astroEscape</c> applies to dynamic values at runtime.</summary>
    private static string HtmlEscape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>
    /// The small, self-contained runtime every compiled <c>.astro</c> module
    /// gets at its top (inlined per module rather than shared via an import —
    /// the same choice <see cref="CssModules.GenerateModule"/> makes for its own
    /// runtime, and for the same reason: it keeps each generated module
    /// independent, with nothing new for the resolver to wire up). Declared
    /// inside the module's own factory function once the bundle assembles it,
    /// so these names never leak into — or collide with — anything else.
    ///
    /// <c>html</c> is a tagged-template function: each interpolated value is
    /// escaped by default (<c>__astroStringify</c> → <c>__astroEscape</c>)
    /// unless it's already-rendered HTML (an <c>__AstroHtml</c> instance — what
    /// a component call and <c>__astroAttrs</c> both return), which is inlined
    /// as-is. This is what lets nested component output compose safely without
    /// being double-escaped, while plain interpolated values stay safe by
    /// default.
    /// </summary>
    private const string Runtime =
        "class __AstroHtml {\n" +
        "  constructor(value) { this.value = value; }\n" +
        "  toString() { return this.value; }\n" +
        "}\n" +
        "function __astroEscape(value) {\n" +
        "  var s = String(value);\n" +
        "  s = s.split(\"&\").join(\"&amp;\");\n" +
        "  s = s.split(\"<\").join(\"&lt;\");\n" +
        "  s = s.split(\">\").join(\"&gt;\");\n" +
        "  s = s.split('\"').join(\"&quot;\");\n" +
        "  s = s.split(\"'\").join(\"&#39;\");\n" +
        "  return s;\n" +
        "}\n" +
        "function __astroStringify(value) {\n" +
        "  if (value == null || value === false || value === true) return \"\";\n" +
        "  if (Array.isArray(value)) return value.map(__astroStringify).join(\"\");\n" +
        "  if (value instanceof __AstroHtml) return value.value;\n" +
        "  return __astroEscape(value);\n" +
        "}\n" +
        "function __astroAttrs(props) {\n" +
        "  var out = \"\";\n" +
        "  props = props || {};\n" +
        "  for (var key in props) {\n" +
        "    var value = props[key];\n" +
        "    if (value == null || value === false) continue;\n" +
        "    out += value === true ? (\" \" + key) : (\" \" + key + '=\"' + __astroEscape(value) + '\"');\n" +
        "  }\n" +
        "  return new __AstroHtml(out);\n" +
        "}\n" +
        "function html(strings, ...values) {\n" +
        "  var out = strings[0];\n" +
        "  for (var i = 0; i < values.length; i++) {\n" +
        "    out += __astroStringify(values[i]) + strings[i + 1];\n" +
        "  }\n" +
        "  return new __AstroHtml(out);\n" +
        "}\n";
}
