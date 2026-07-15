namespace NetPack.Graph;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
// `Node` is ambiguous in this namespace (NetPack.Graph.Node vs the AST node), so
// alias the AST node type for the pattern-collection helper below.
using AstNode = NetPack.Syntax.Ast.Node;

/// <summary>A single <c>&lt;style&gt;</c> block of a Vue SFC.</summary>
public sealed class VueStyleBlock
{
    /// <summary>The (already preprocessed and, when scoped, selector-rewritten) CSS text.</summary>
    public string Css { get; init; } = "";

    /// <summary>Whether this block carried the <c>scoped</c> attribute.</summary>
    public bool Scoped { get; init; }
}

/// <summary>
/// The extracted top-level blocks of a Vue single-file component. IO (reading the
/// file, resolving <c>src</c>, preprocessing <c>lang</c>) happens in the caller;
/// this record is a pure, already-resolved view of the SFC.
/// </summary>
public sealed class VueDescriptor
{
    /// <summary>The <c>&lt;template&gt;</c> markup, or null when the SFC has no template.</summary>
    public string? Template { get; init; }

    /// <summary>The classic <c>&lt;script&gt;</c> block contents, or null when absent.</summary>
    public string? Script { get; init; }

    /// <summary>The <c>&lt;script setup&gt;</c> block contents, or null when absent.</summary>
    public string? ScriptSetup { get; init; }

    /// <summary>The <c>&lt;style&gt;</c> blocks, in source order.</summary>
    public IReadOnlyList<VueStyleBlock> Styles { get; init; } = [];

    /// <summary>Project-relative path, used for the scope id and the <c>__file</c> marker.</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>The <c>data-v-*</c> scope id used for scoped styles.</summary>
    public string ScopeId { get; init; } = "";
}

/// <summary>
/// Turns the extracted blocks of a Vue single-file component into a virtual
/// JavaScript module, mirroring what <c>@vue/compiler-sfc</c> produces but staying
/// entirely native. A classic <c>&lt;script&gt;</c> default export becomes the
/// component; a <c>&lt;script setup&gt;</c> block is compiled into a
/// <c>setup()</c> function (imports hoisted, top-level bindings returned for the
/// template, and the <c>defineProps</c> / <c>defineEmits</c> / <c>defineExpose</c>
/// / <c>defineOptions</c> / <c>withDefaults</c> macros expanded). The template is
/// attached as a string (compiled by Vue's runtime compiler) and scoped styles set
/// <c>__scopeId</c>.
/// </summary>
public static class VueSfc
{
    /// <summary>The local the component object is bound to inside the generated module.</summary>
    public const string ComponentLocal = "__sfc_main";

    private static readonly ParserOptions ScriptOptions = new() { Tolerant = true, TypeScript = true, Jsx = true };

    public static string Generate(VueDescriptor sfc)
    {
        var sb = new StringBuilder();

        // 1) Build the component object bound to __sfc_main from the script block(s).
        if (!string.IsNullOrWhiteSpace(sfc.ScriptSetup))
        {
            sb.Append(BuildSetupComponent(sfc.ScriptSetup!, sfc.Script));
        }
        else
        {
            sb.Append(BuildScript(sfc.Script)).Append(";\n");
        }

        // 2) Inject the (already preprocessed / scoped) CSS of every <style> block.
        var css = string.Concat(sfc.Styles.Select(s => s.Css));

        if (css.Length > 0)
        {
            sb.Append("(function () {\n");
            sb.Append("  const __css = ").Append(CssModules.JsString(css)).Append(";\n");
            sb.Append("  if (typeof document !== \"undefined\") {\n");
            sb.Append("    const __el = document.createElement(\"style\");\n");
            sb.Append("    __el.textContent = __css;\n");
            sb.Append("    document.head.appendChild(__el);\n");
            sb.Append("  }\n");
            sb.Append("})();\n");
        }

        // 3) Scoped styles: tag the component so the runtime stamps the scope id
        //    onto the elements it renders.
        if (sfc.Styles.Any(s => s.Scoped) && !string.IsNullOrEmpty(sfc.ScopeId))
        {
            sb.Append(ComponentLocal).Append(".__scopeId = ").Append(CssModules.JsString(sfc.ScopeId)).Append(";\n");
        }

        // 4) The template is handed to Vue as a string (runtime compilation).
        if (sfc.Template is { } template)
        {
            sb.Append(ComponentLocal).Append(".template = ").Append(CssModules.JsString(template)).Append(";\n");
        }

        sb.Append(ComponentLocal).Append(".__file = ").Append(CssModules.JsString(sfc.RelativePath)).Append(";\n");
        sb.Append("export default ").Append(ComponentLocal).Append(";\n");
        return sb.ToString();
    }

    /// <summary>
    /// Rebinds a classic script's <c>export default &lt;expr&gt;</c> to
    /// <c>const __sfc_main = &lt;expr&gt;</c> so the rest of the module can decorate
    /// it, while preserving every other statement verbatim.
    /// </summary>
    private static string BuildScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return $"const {ComponentLocal} = {{}}";
        }

        var module = Parser.ParseModule(script, "sfc-script.js", ScriptOptions);
        var def = module.Body.OfType<ExportDefaultDeclaration>().FirstOrDefault();

        if (def is null)
        {
            return $"{script}\nconst {ComponentLocal} = {{}}";
        }

        var head = script[..def.Start];
        var declaration = script[def.Declaration.Start..];
        return $"{head}const {ComponentLocal} = {declaration}";
    }

    // --- <script setup> compilation ---------------------------------------

    private sealed class Edit(int start, int end, string text)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public string Text { get; } = text;
    }

    /// <summary>
    /// Compiles a <c>&lt;script setup&gt;</c> block (plus an optional classic
    /// <c>&lt;script&gt;</c>) into <c>const __sfc_main = { … , setup() { … } };</c>.
    /// </summary>
    private static string BuildSetupComponent(string setup, string? classic)
    {
        var module = Parser.ParseModule(setup, "sfc-setup.js", ScriptOptions);

        var hoistedImports = new List<string>();
        var bindings = new List<string>();      // top-level names exposed to the template
        var edits = new List<Edit>();           // rewrites applied to the setup body text
        var seenBindings = new HashSet<string>();

        string? propsExpr = null;
        string? emitsExpr = null;
        string? optionsExpr = null;
        var usesMergeDefaults = false;

        void AddBinding(string name)
        {
            if (!string.IsNullOrEmpty(name) && seenBindings.Add(name))
            {
                bindings.Add(name);
            }
        }

        foreach (var statement in module.Body)
        {
            switch (statement)
            {
                case ImportDeclaration imp when !imp.TypeOnly:
                    hoistedImports.Add(setup[imp.Start..imp.End]);
                    edits.Add(new Edit(imp.Start, imp.End, ""));
                    foreach (var specifier in imp.Specifiers)
                    {
                        if (specifier is ImportSpecifier { TypeOnly: true })
                        {
                            continue;
                        }

                        AddBinding(specifier.Local.Name);
                    }
                    break;

                case ImportDeclaration imp:
                    // `import type` — erase entirely.
                    edits.Add(new Edit(imp.Start, imp.End, ""));
                    break;

                case VariableStatement variable:
                    foreach (var declarator in variable.Declarations)
                    {
                        CollectPatternNames(declarator.Id, AddBinding);
                        RewriteMacroCall(setup, declarator.Init, edits, ref propsExpr, ref emitsExpr, ref usesMergeDefaults);
                    }
                    break;

                case FunctionDeclaration { Id: { } fnId }:
                    AddBinding(fnId.Name);
                    break;

                case ClassDeclaration { Id: { } clsId }:
                    AddBinding(clsId.Name);
                    break;

                case ExpressionStatement { Expression: CallExpression call }:
                    HandleMacroStatement(setup, statement, call, edits,
                        ref propsExpr, ref emitsExpr, ref optionsExpr, ref usesMergeDefaults);
                    break;
            }
        }

        var body = ApplyEdits(setup, edits);

        var sb = new StringBuilder();

        foreach (var importText in hoistedImports)
        {
            sb.Append(importText).Append('\n');
        }

        if (usesMergeDefaults)
        {
            sb.Append(MergeDefaultsHelper);
        }

        // An optional classic <script> contributes hoisted statements and a base
        // options object that the setup component is spread on top of.
        string? baseLocal = null;

        if (!string.IsNullOrWhiteSpace(classic))
        {
            sb.Append(BuildClassicBase(classic!, out baseLocal));
        }

        sb.Append("const ").Append(ComponentLocal).Append(" = {\n");

        if (baseLocal is not null)
        {
            sb.Append("  ...").Append(baseLocal).Append(",\n");
        }

        if (optionsExpr is not null)
        {
            sb.Append("  ...(").Append(optionsExpr).Append("),\n");
        }

        if (propsExpr is not null)
        {
            sb.Append("  props: ").Append(propsExpr).Append(",\n");
        }

        if (emitsExpr is not null)
        {
            sb.Append("  emits: ").Append(emitsExpr).Append(",\n");
        }

        sb.Append("  setup(__props, __ctx) {\n");
        sb.Append(body).Append('\n');
        sb.Append("    return { ").Append(string.Join(", ", bindings)).Append(" };\n");
        sb.Append("  }\n");
        sb.Append("};\n");
        return sb.ToString();
    }

    /// <summary>Hoists a classic script's statements and captures its default export
    /// as a base options local that the setup component spreads over.</summary>
    private static string BuildClassicBase(string classic, out string baseLocal)
    {
        baseLocal = "__sfc_base";
        var module = Parser.ParseModule(classic, "sfc-classic.js", ScriptOptions);
        var def = module.Body.OfType<ExportDefaultDeclaration>().FirstOrDefault();

        if (def is null)
        {
            return $"{classic}\nconst {baseLocal} = {{}};\n";
        }

        var head = classic[..def.Start];
        var tail = classic[def.End..];
        var expr = classic[def.Declaration.Start..def.Declaration.End];
        return $"{head}const {baseLocal} = {expr};{tail}\n";
    }

    private static void HandleMacroStatement(
        string source, Statement statement, CallExpression call, List<Edit> edits,
        ref string? propsExpr, ref string? emitsExpr, ref string? optionsExpr, ref bool usesMergeDefaults)
    {
        var name = (call.Callee as Identifier)?.Name;

        switch (name)
        {
            case "defineOptions":
                optionsExpr = ArgText(source, call, 0);
                edits.Add(new Edit(statement.Start, statement.End, ""));
                break;

            case "defineExpose":
                // Keep the call but retarget it to the setup context.
                edits.Add(new Edit(call.Callee.Start, call.Callee.End, "__ctx.expose"));
                break;

            default:
                RewriteMacroCall(source, call, edits, ref propsExpr, ref emitsExpr, ref usesMergeDefaults);
                break;
        }
    }

    /// <summary>
    /// Detects and rewrites a <c>defineProps</c> / <c>withDefaults</c> /
    /// <c>defineEmits</c> call used as an initializer or bare expression: the call
    /// is replaced with the value the macro yields at runtime and the props/emits
    /// definitions are captured for the component options.
    /// </summary>
    private static void RewriteMacroCall(
        string source, Expression? expression, List<Edit> edits,
        ref string? propsExpr, ref string? emitsExpr, ref bool usesMergeDefaults)
    {
        if (expression is not CallExpression call || call.Callee is not Identifier callee)
        {
            return;
        }

        switch (callee.Name)
        {
            case "defineProps":
                propsExpr = ArgText(source, call, 0) ?? "{}";
                edits.Add(new Edit(call.Start, call.End, "__props"));
                break;

            case "withDefaults" when call.Arguments.Count > 0 && call.Arguments[0] is CallExpression inner:
                var innerArg = ArgText(source, inner, 0) ?? "{}";
                var defaults = ArgText(source, call, 1) ?? "{}";
                propsExpr = $"__mergeDefaults({innerArg}, {defaults})";
                usesMergeDefaults = true;
                edits.Add(new Edit(call.Start, call.End, "__props"));
                break;

            case "defineEmits":
                emitsExpr = ArgText(source, call, 0) ?? "[]";
                edits.Add(new Edit(call.Start, call.End, "__ctx.emit"));
                break;
        }
    }

    private static string? ArgText(string source, CallExpression call, int index)
        => index < call.Arguments.Count ? source[call.Arguments[index].Start..call.Arguments[index].End] : null;

    /// <summary>Collects the identifier names bound by a (possibly destructuring)
    /// binding target.</summary>
    private static void CollectPatternNames(AstNode target, System.Action<string> add)
    {
        switch (target)
        {
            case Identifier id:
                add(id.Name);
                break;

            case ObjectExpression obj:
                foreach (var member in obj.Properties)
                {
                    if (member is Property property)
                    {
                        CollectPatternNames(property.Value ?? property.Key, add);
                    }
                    else if (member is SpreadElement spread)
                    {
                        CollectPatternNames(spread.Argument, add);
                    }
                }
                break;

            case ArrayExpression arr:
                foreach (var element in arr.Elements)
                {
                    if (element is not null)
                    {
                        CollectPatternNames(element, add);
                    }
                }
                break;

            case SpreadElement spread:
                CollectPatternNames(spread.Argument, add);
                break;

            case AssignmentExpression assignment:
                // Destructuring default, e.g. `{ a = 1 }` / `[b = 2]`.
                CollectPatternNames(assignment.Left, add);
                break;
        }
    }

    private static string ApplyEdits(string source, List<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return source;
        }

        var sb = new StringBuilder(source.Length);
        var cursor = 0;

        foreach (var edit in edits.OrderBy(e => e.Start))
        {
            if (edit.Start < cursor)
            {
                continue; // skip overlapping edits defensively
            }

            sb.Append(source, cursor, edit.Start - cursor);
            sb.Append(edit.Text);
            cursor = edit.End;
        }

        sb.Append(source, cursor, source.Length - cursor);
        return sb.ToString();
    }

    private const string MergeDefaultsHelper =
        "function __mergeDefaults(raw, defaults) {\n" +
        "  const props = Array.isArray(raw) ? raw.reduce((n, p) => (n[p] = {}, n), {}) : Object.assign({}, raw);\n" +
        "  for (const key in defaults) {\n" +
        "    const val = props[key];\n" +
        "    if (val && typeof val === \"object\" && !Array.isArray(val)) val.default = defaults[key];\n" +
        "    else props[key] = { type: val, default: defaults[key] };\n" +
        "  }\n" +
        "  return props;\n" +
        "}\n";
}
