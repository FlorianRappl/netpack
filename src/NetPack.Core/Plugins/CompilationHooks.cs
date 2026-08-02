namespace NetPack.Plugins;

using NetPack.Graph;
using NetPack.Graph.Bundles;

/// <summary>
/// Hook containers for the compilation phase. Plugins can tap into these hooks
/// to modify the compilation at various stages.
/// </summary>
public class CompilationHooks
{
    /// <summary>Called when the compilation is being sealed (finalized).</summary>
    public SeriesHook<CompilationContext> Seal { get; } = new();

    /// <summary>Called to optimize dependencies (tree-shaking, etc.).</summary>
    public SeriesBailHook<CompilationContext, bool> OptimizeDependencies { get; } = new();

    /// <summary>Called to optimize chunks (code splitting, chunk merging, etc.).</summary>
    public SeriesBailHook<CompilationContext, bool> OptimizeChunks { get; } = new();

    /// <summary>Called to optimize modules (minification, dead code elimination, etc.).</summary>
    public SeriesBailHook<CompilationContext, bool> OptimizeModules { get; } = new();

    /// <summary>Called after code generation is complete.</summary>
    public SeriesHook<CompilationContext> AfterCodeGeneration { get; } = new();

    /// <summary>Called to process assets (CSS extraction, minification, etc.).</summary>
    public SeriesHook<CompilationContext> ProcessAssets { get; } = new();

    /// <summary>Called after assets are processed.</summary>
    public SeriesHook<CompilationContext> AfterProcessAssets { get; } = new();

    /// <summary>Called after the compilation is sealed.</summary>
    public SeriesHook<CompilationContext> AfterSeal { get; } = new();

    /// <summary>Called to compute content hashes for bundles.</summary>
    public SeriesHook<CompilationContext> ContentHash { get; } = new();
}

/// <summary>
/// Hook containers for the compiler phase. Plugins can tap into these hooks
/// to modify the compiler behavior at various stages.
/// </summary>
public class CompilerHooks
{
    /// <summary>Called when a new compilation is created.</summary>
    public SeriesHook<CompilationContext> Compilation { get; } = new();

    /// <summary>Called to start the build process.</summary>
    public SeriesHook<CompilationContext> Make { get; } = new();

    /// <summary>Called after the build process is complete.</summary>
    public SeriesHook<CompilationContext> FinishMake { get; } = new();

    /// <summary>Called to check if assets should be emitted. Return false to skip.</summary>
    public SeriesBailHook<CompilationContext, bool> ShouldEmit { get; } = new();

    /// <summary>Called before assets are emitted.</summary>
    public SeriesHook<CompilationContext> Emit { get; } = new();

    /// <summary>Called after assets are emitted.</summary>
    public SeriesHook<CompilationContext> AfterEmit { get; } = new();

    /// <summary>Called when the compilation succeeds.</summary>
    public SeriesHook<CompilationContext> Done { get; } = new();

    /// <summary>Called when the compilation fails.</summary>
    public SeriesHook<CompilationContext> Failed { get; } = new();
}

/// <summary>
/// Context passed to hooks during compilation.
/// </summary>
public class CompilationContext
{
    /// <summary>The bundler context with all fragments, bundles, and assets.</summary>
    public required BundlerContext BundlerContext { get; init; }

    /// <summary>The output options for this compilation.</summary>
    public required OutputOptions OutputOptions { get; init; }

    /// <summary>Whether this is a development build (dev server).</summary>
    public bool IsDevelopment { get; init; }

    /// <summary>Whether this is a production build.</summary>
    public bool IsProduction => !IsDevelopment;

    /// <summary>Custom state bag for plugins to share data during compilation.</summary>
    public Dictionary<string, object> State { get; } = [];
}
