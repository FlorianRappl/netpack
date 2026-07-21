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
}
