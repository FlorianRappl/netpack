namespace NetPack.Graph;

/// <summary>The JavaScript module format ("envelope") a JS bundle is emitted in.</summary>
public enum ModuleFormat
{
    /// <summary>Native ECMAScript modules — <c>import</c> / <c>export</c> (default).</summary>
    Esm,

    /// <summary>CommonJS — <c>require</c> / <c>module.exports</c>.</summary>
    CommonJs,

    /// <summary>Universal Module Definition.</summary>
    Umd,

    /// <summary>SystemJS <c>System.register</c>.</summary>
    SystemJs,
}

public record OutputOptions
{
    public required bool IsOptimizing { get; init; }

    public required bool IsReloading { get; init; }

    /// <summary>Emit a Source Map v3 next to each JS bundle and a
    /// <c>sourceMappingURL</c> comment pointing at it.</summary>
    public bool WithSourceMaps { get; init; }

    /// <summary>The output module format each JS bundle is wrapped in
    /// (default <see cref="ModuleFormat.Esm"/>).</summary>
    public ModuleFormat Format { get; init; } = ModuleFormat.Esm;

    /// <summary>
    /// The naming template for emitted JS/CSS bundles, with <c>[name]</c> and
    /// <c>[hash]</c> placeholders (the <c>--entry-names</c> option). The default
    /// <c>[name]</c> keeps the entry's own name. Including <c>[hash]</c> appends a
    /// content hash for cache-busting, e.g. <c>[name]-[hash]</c> →
    /// <c>app-1a2b3c.js</c>. The entry HTML document keeps its name regardless.
    /// </summary>
    public string EntryNames { get; init; } = "[name]";

    /// <summary>
    /// A base path/URL prepended to every reference to an emitted file — bundle
    /// chunks, assets, and the script/link/img targets in the HTML shell (the
    /// <c>--public-path</c> option). Empty keeps references document-relative.
    /// </summary>
    public string PublicPath { get; init; } = "";
}
