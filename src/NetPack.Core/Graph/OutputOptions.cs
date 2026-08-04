namespace NetPack.Graph;

/// <summary>How third-party license/legal comments are collected and emitted.</summary>
public enum LicenseMode
{
    /// <summary>Collect legal comments and place the relevant ones in each bundle's
    /// head, after any banner (default).</summary>
    Preamble,

    /// <summary>Do not collect or emit licenses.</summary>
    Skip,

    /// <summary>Collect package licenses into a <c>licenses.json</c> in the output
    /// directory (suffixed if a file of that name already exists).</summary>
    Json,

    /// <summary>Collect package licenses into a <c>licenses.spdx</c> (SPDX
    /// tag-value document) in the output directory.</summary>
    Spdx,
}

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

    /// <summary>
    /// Arbitrary text placed on the very first line of each entry JS bundle,
    /// followed by a newline (the <c>--banner</c> option). Typically a
    /// license/copyright comment or a runtime pragma. Empty (the default) emits
    /// nothing. Entry bundles receive the banner; shared split chunks do not.
    /// </summary>
    public string Banner { get; init; } = "";

    /// <summary>
    /// When true, <c>&lt;link rel="modulepreload"&gt;</c> directives are emitted
    /// for shared JS bundles that entry scripts import. The browser fetches these
    /// early, before the module graph needs them. Defaults to <c>true</c>.
    /// Disabled with <c>--no-preload</c>.
    /// </summary>
    public bool EnableModulePreload { get; init; } = true;

    /// <summary>
    /// Maximum file size in bytes for assets to be inlined as data URIs instead
    /// of emitted as separate files. 0 (the default) disables inlining. When set
    /// (e.g. 4096), any asset file up to that size is embedded as a
    /// <c>data:…;base64,…</c> URI wherever it is referenced — in JS imports,
    /// CSS <c>url()</c> values, and HTML <c>src</c>/<c>href</c> attributes —
    /// saving a network roundtrip for small assets like icons, fonts, or tiny
    /// images.
    /// </summary>
    public int InlineLimit { get; init; } = 0;

    /// <summary>
    /// How third-party license/legal comments are handled (the <c>--licenses</c>
    /// option). <see cref="LicenseMode.Preamble"/> (default) preserves legal
    /// comments in each bundle's head; <see cref="LicenseMode.Json"/> /
    /// <see cref="LicenseMode.Spdx"/> emit a license manifest file instead;
    /// <see cref="LicenseMode.Skip"/> disables collection.
    /// </summary>
    public LicenseMode Licenses { get; init; } = LicenseMode.Preamble;
}
