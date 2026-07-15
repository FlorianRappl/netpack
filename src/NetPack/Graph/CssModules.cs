namespace NetPack.Graph;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Css;
using AngleSharp.Css.Dom;

/// <summary>
/// Implements CSS-module handling: when a JavaScript/TypeScript module imports a
/// CSS file with named or default bindings, the CSS file is treated as a module.
/// Its class selectors are rewritten to hashed, collision-free names and the
/// import resolves to an object mapping each original class name to its hashed
/// counterpart. The (rewritten) CSS is injected at runtime via a &lt;style&gt;
/// element so the styles still apply.
/// </summary>
public static class CssModules
{
    // Matches a class selector `.name` (interpreted regex keeps this AOT-safe).
    private static readonly Regex ClassSelectorRegex = new(@"\.(-?[A-Za-z_][A-Za-z0-9_-]*)");

    // Reserved words that cannot be used as `export const <name>` bindings; such
    // class names are still reachable through the default export map.
    private static readonly HashSet<string> Reserved =
    [
        "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "enum", "export", "extends", "false",
        "finally", "for", "function", "if", "import", "in", "instanceof", "new",
        "null", "return", "super", "switch", "this", "throw", "true", "try",
        "typeof", "var", "void", "while", "with", "yield", "let", "static",
        "await", "async", "implements", "interface", "package", "private",
        "protected", "public",
    ];

    /// <summary>
    /// Rewrites the class selectors of <paramref name="sheet"/> in place (when
    /// <paramref name="hashClasses"/> is set), returning the original→hashed name
    /// map and the serialized CSS text.
    /// </summary>
    public static (Dictionary<string, string> Map, string Css) Rewrite(
        ICssStyleSheet sheet, string relativePath, bool hashClasses)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (hashClasses)
        {
            var suffix = Hash.Short(relativePath);
            RewriteRules(sheet.Rules, suffix, map);
        }

        using var writer = new StringWriter();
        sheet.ToCss(writer, new MinifyStyleFormatter());
        return (map, writer.ToString());
    }

    private static void RewriteRules(IEnumerable<ICssRule> rules, string suffix, Dictionary<string, string> map)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssStyleRule style)
            {
                style.SelectorText = ClassSelectorRegex.Replace(style.SelectorText, match =>
                {
                    var name = match.Groups[1].Value;

                    if (!map.TryGetValue(name, out var hashed))
                    {
                        hashed = $"{name}_{suffix}";
                        map[name] = hashed;
                    }

                    return $".{hashed}";
                });
            }
            else if (rule is ICssGroupingRule grouping)
            {
                // @media / @supports blocks nest style rules.
                RewriteRules(grouping.Rules, suffix, map);
            }
        }
    }

    /// <summary>
    /// Rewrites every selector of <paramref name="sheet"/> to also require the
    /// <paramref name="scopeAttribute"/> (e.g. <c>[data-v-1a2b3c]</c>), implementing
    /// Vue's <c>&lt;style scoped&gt;</c>. Returns the serialized, scoped CSS.
    /// </summary>
    public static string ApplyScope(ICssStyleSheet sheet, string scopeAttribute)
    {
        ScopeRules(sheet.Rules, scopeAttribute);
        using var writer = new StringWriter();
        sheet.ToCss(writer, new MinifyStyleFormatter());
        return writer.ToString();
    }

    private static void ScopeRules(IEnumerable<ICssRule> rules, string scopeAttribute)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssStyleRule style)
            {
                var selectors = style.SelectorText.Split(',');
                style.SelectorText = string.Join(", ", selectors.Select(s => AppendScope(s.Trim(), scopeAttribute)));
            }
            else if (rule is ICssGroupingRule grouping)
            {
                ScopeRules(grouping.Rules, scopeAttribute);
            }
        }
    }

    private static string AppendScope(string selector, string scopeAttribute)
    {
        // Keep the scope attribute in front of a trailing pseudo-element (::before,
        // ::after, …) so the selector stays valid; otherwise append to the end.
        var idx = selector.IndexOf("::", System.StringComparison.Ordinal);
        return idx >= 0
            ? string.Concat(selector[..idx], scopeAttribute, selector[idx..])
            : string.Concat(selector, scopeAttribute);
    }

    /// <summary>
    /// Builds the virtual JavaScript module for a CSS import: it injects the CSS
    /// at runtime and exports the class-name map (named exports for identifier-safe
    /// names, plus a default export object covering every class).
    /// </summary>
    public static string GenerateModule(string css, IReadOnlyDictionary<string, string> map)
    {
        var sb = new StringBuilder();

        sb.Append("const __css = ").Append(JsString(css)).Append(";\n");
        sb.Append("if (typeof document !== \"undefined\") {\n");
        sb.Append("  const __el = document.createElement(\"style\");\n");
        sb.Append("  __el.textContent = __css;\n");
        sb.Append("  document.head.appendChild(__el);\n");
        sb.Append("}\n");

        foreach (var (name, hashed) in map)
        {
            if (IsIdentifier(name))
            {
                sb.Append("export const ").Append(name).Append(" = ").Append(JsString(hashed)).Append(";\n");
            }
        }

        sb.Append("export default { ");
        var first = true;
        foreach (var (name, hashed) in map)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(JsString(name)).Append(": ").Append(JsString(hashed));
        }
        sb.Append(" };\n");

        return sb.ToString();
    }

    // Encodes a string as a JavaScript/JSON double-quoted literal. Written by
    // hand (rather than via JsonSerializer) so it stays trim/AOT-safe. Also
    // escapes U+2028/U+2029, which are legal in JSON but break JS string literals.
    public static string JsString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || Reserved.Contains(name))
        {
            return false;
        }

        if (!(char.IsLetter(name[0]) || name[0] is '_' or '$'))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] is '_' or '$'))
            {
                return false;
            }
        }

        return true;
    }
}
