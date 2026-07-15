namespace NetPack.Graph;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetPack.Syntax;
using NetPack.Syntax.Ast;

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

    /// <summary>The <c>&lt;style&gt;</c> blocks, in source order.</summary>
    public IReadOnlyList<VueStyleBlock> Styles { get; init; } = [];

    /// <summary>Project-relative path, used for the scope id and the <c>__file</c> marker.</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>The <c>data-v-*</c> scope id used for scoped styles.</summary>
    public string ScopeId { get; init; } = "";
}

/// <summary>
/// Turns the extracted blocks of a Vue single-file component into a virtual
/// JavaScript module. The classic <c>&lt;script&gt;</c> default export becomes the
/// component object; the template is attached as a string (compiled by Vue's
/// runtime compiler); scoped styles set <c>__scopeId</c> and are injected at
/// runtime. Mirrors the assembly that <c>@vue/compiler-sfc</c> performs, but stays
/// entirely native so no Node round-trip is required.
/// </summary>
public static class VueSfc
{
    /// <summary>The local the component object is bound to inside the generated module.</summary>
    public const string ComponentLocal = "__sfc_main";

    public static string Generate(VueDescriptor sfc)
    {
        var sb = new StringBuilder();

        // 1) The <script> block, with its `export default X` rebound to a local we
        //    can then decorate with the template, scope id and styles. The trailing
        //    semicolon is essential: without it the following style IIFE `(...)()`
        //    would be parsed as a call on the component object.
        sb.Append(BuildScript(sfc.Script)).Append(";\n");

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
    /// Rebinds the script's <c>export default &lt;expr&gt;</c> to
    /// <c>const __sfc_main = &lt;expr&gt;</c> so the rest of the module can decorate
    /// it, while preserving every other statement (imports, helpers) verbatim. When
    /// there is no default export a bare component object is created instead.
    /// </summary>
    private static string BuildScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return $"const {ComponentLocal} = {{}};";
        }

        var options = new ParserOptions { Tolerant = true, TypeScript = true };
        var module = Parser.ParseModule(script, "sfc-script.js", options);
        var def = module.Body.OfType<ExportDefaultDeclaration>().FirstOrDefault();

        if (def is null)
        {
            return $"{script}\nconst {ComponentLocal} = {{}};";
        }

        // Splice out the `export default ` keyword prefix (everything from the
        // statement start up to the declaration) and replace it with the binding.
        // This works whether the declaration is an object, a call (defineComponent),
        // or a named function/class — all become valid right-hand sides.
        var head = script[..def.Start];
        var declaration = script[def.Declaration.Start..];
        return $"{head}const {ComponentLocal} = {declaration}";
    }
}
