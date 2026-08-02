namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using NetPack.Graph;

/// <summary>
/// Compiler-level hooks — the lifecycle of a whole bundler run, which may drive
/// several compilations over its lifetime (e.g. under <c>serve</c> or
/// <c>bundle --watch</c>). Pre-compilation hooks receive a
/// <see cref="CompilerContext"/>; once a compilation exists the hooks carry the
/// richer <see cref="CompilationContext"/>. Taps run in <see cref="IHookTap.Stage"/>
/// order (lowest first). Inspired by webpack/rspack's <c>Compiler</c> hooks.
/// </summary>
public class CompilerHooks
{
    /// <summary>Fired once when the compiler is created, before any run.</summary>
    public SyncHook<CompilerContext> Initialize { get; } = new();

    /// <summary>Before a (one-shot) run begins.</summary>
    public SeriesHook<CompilerContext> BeforeRun { get; } = new();

    /// <summary>A one-shot run begins.</summary>
    public SeriesHook<CompilerContext> Run { get; } = new();

    /// <summary>A watch-triggered run begins (dev server / <c>--watch</c>).</summary>
    public SeriesHook<CompilerContext> WatchRun { get; } = new();

    /// <summary>Before a compilation is created.</summary>
    public SeriesHook<CompilerContext> BeforeCompile { get; } = new();

    /// <summary>A compilation is about to be created (notification).</summary>
    public SyncHook<CompilerContext> Compile { get; } = new();

    /// <summary>A compilation has been created — fires before <see cref="Compilation"/>
    /// and is the place to tap this compilation's own hooks.</summary>
    public SyncHook<CompilationContext> ThisCompilation { get; } = new();

    /// <summary>A compilation has been created — the main registration point.</summary>
    public SeriesHook<CompilationContext> Compilation { get; } = new();

    /// <summary>Start of the build phase (module graph construction).</summary>
    public SeriesHook<CompilationContext> Make { get; } = new();

    /// <summary>The build phase has finished.</summary>
    public SeriesHook<CompilationContext> FinishMake { get; } = new();

    /// <summary>A compilation has finished (sealed), before emit.</summary>
    public SeriesHook<CompilationContext> AfterCompile { get; } = new();

    /// <summary>Whether assets should be emitted at all. Return <c>false</c> to skip.</summary>
    public SeriesBailHook<CompilationContext, bool> ShouldEmit { get; } = new();

    /// <summary>Before assets are written to the output.</summary>
    public SeriesHook<CompilationContext> Emit { get; } = new();

    /// <summary>After assets have been written to the output.</summary>
    public SeriesHook<CompilationContext> AfterEmit { get; } = new();

    /// <summary>The run finished successfully.</summary>
    public SeriesHook<CompilationContext> Done { get; } = new();

    /// <summary>The run failed; <see cref="CompilerContext.Error"/> holds the cause.</summary>
    public SyncHook<CompilerContext> Failed { get; } = new();

    /// <summary>Watch mode: a change invalidated the current build.</summary>
    public SyncHook<CompilerContext> Invalid { get; } = new();

    /// <summary>Watch mode: watching has stopped.</summary>
    public SyncHook<CompilerContext> WatchClose { get; } = new();

    /// <summary>The compiler is shutting down (dispose resources here).</summary>
    public SeriesHook<CompilerContext> Shutdown { get; } = new();
}

/// <summary>
/// Compilation-level hooks — the phases within a single compilation: building
/// modules, optimizing the graph, assigning ids, generating code, processing
/// assets, and sealing. Taps run in <see cref="IHookTap.Stage"/> order (lowest
/// first); <see cref="ProcessAssets"/> in particular is meant to be tapped with a
/// <see cref="ProcessAssetsStage"/> value. Inspired by webpack/rspack's
/// <c>Compilation</c> hooks.
/// </summary>
public class CompilationHooks
{
    // -- module build lifecycle -------------------------------------------

    /// <summary>A module is about to be built (parsed / transformed).</summary>
    public SeriesHook<ModuleBuildContext> BuildModule { get; } = new();

    /// <summary>A module built successfully.</summary>
    public SeriesHook<ModuleBuildContext> SucceedModule { get; } = new();

    /// <summary>A module failed to build; <see cref="CompilerContext.Error"/> holds
    /// the cause.</summary>
    public SeriesHook<ModuleBuildContext> FailedModule { get; } = new();

    /// <summary>Watch mode: a module was reused unchanged from the previous build.</summary>
    public SeriesHook<ModuleBuildContext> StillValidModule { get; } = new();

    /// <summary>All modules have been built.</summary>
    public SeriesHook<CompilationContext> FinishModules { get; } = new();

    // -- optimization -----------------------------------------------------

    /// <summary>Start of the optimization phase.</summary>
    public SeriesHook<CompilationContext> Optimize { get; } = new();

    /// <summary>Optimize modules (minification, dead-code elimination, …).</summary>
    public SeriesHook<CompilationContext> OptimizeModules { get; } = new();

    /// <summary>After modules were optimized.</summary>
    public SeriesHook<CompilationContext> AfterOptimizeModules { get; } = new();

    /// <summary>Optimize chunks (code splitting, chunk merging, …).</summary>
    public SeriesHook<CompilationContext> OptimizeChunks { get; } = new();

    /// <summary>After chunks were optimized.</summary>
    public SeriesHook<CompilationContext> AfterOptimizeChunks { get; } = new();

    /// <summary>Optimize the chunk + module graph together.</summary>
    public SeriesHook<CompilationContext> OptimizeTree { get; } = new();

    /// <summary>Optimize the modules within each chunk.</summary>
    public SeriesHook<CompilationContext> OptimizeChunkModules { get; } = new();

    /// <summary>Optimize dependencies (usage/tree-shaking analysis).</summary>
    public SeriesHook<CompilationContext> OptimizeDependencies { get; } = new();

    /// <summary>After dependencies were optimized.</summary>
    public SeriesHook<CompilationContext> AfterOptimizeDependencies { get; } = new();

    // -- ids --------------------------------------------------------------

    /// <summary>Assign / rewrite module ids.</summary>
    public SeriesHook<CompilationContext> ModuleIds { get; } = new();

    /// <summary>Assign / rewrite chunk ids.</summary>
    public SeriesHook<CompilationContext> ChunkIds { get; } = new();

    // -- code generation & assets -----------------------------------------

    /// <summary>Code generation for all modules/chunks is complete.</summary>
    public SeriesHook<CompilationContext> AfterCodeGeneration { get; } = new();

    /// <summary>Add assets that aren't derived from a chunk (copied files, …).</summary>
    public SeriesHook<CompilationContext> AdditionalAssets { get; } = new();

    /// <summary>Process/transform emitted assets. Tap with a
    /// <see cref="ProcessAssetsStage"/> to order relative to other passes.</summary>
    public SeriesHook<CompilationContext> ProcessAssets { get; } = new();

    /// <summary>After all asset processing has run.</summary>
    public SeriesHook<CompilationContext> AfterProcessAssets { get; } = new();

    // -- sealing ----------------------------------------------------------

    /// <summary>The compilation is being sealed (finalized).</summary>
    public SeriesHook<CompilationContext> Seal { get; } = new();

    /// <summary>Compute content hashes for bundles.</summary>
    public SeriesHook<CompilationContext> ContentHash { get; } = new();

    /// <summary>The compilation has been sealed.</summary>
    public SeriesHook<CompilationContext> AfterSeal { get; } = new();
}

/// <summary>
/// Context for compiler-level hooks (those that fire before a compilation exists).
/// A shared <see cref="State"/> bag lets taps thread data across the run.
/// </summary>
public class CompilerContext
{
    /// <summary>The output options for this run. Null before a compilation is
    /// created (e.g. in the <see cref="CompilerHooks.BeforeCompile"/> phase).</summary>
    public OutputOptions? OutputOptions { get; init; }

    /// <summary>Whether this is a development build (dev server).</summary>
    public bool IsDevelopment { get; init; }

    /// <summary>Whether this is a production build.</summary>
    public bool IsProduction => !IsDevelopment;

    /// <summary>Custom state bag for taps to share data during the run.</summary>
    public Dictionary<string, object> State { get; } = [];

    /// <summary>The failure cause on the <see cref="CompilerHooks.Failed"/> /
    /// <see cref="CompilationHooks.FailedModule"/> paths; otherwise null.</summary>
    public Exception? Error { get; set; }
}

/// <summary>
/// Context for compilation-level hooks — adds the <see cref="BundlerContext"/>
/// (fragments, bundles, assets) to the compiler-level state.
/// </summary>
public class CompilationContext : CompilerContext
{
    /// <summary>The bundler context with all fragments, bundles, and assets.</summary>
    public required BundlerContext BundlerContext { get; init; }
}

/// <summary>
/// Context for the per-module build hooks — adds the specific module being built.
/// </summary>
public class ModuleBuildContext : CompilationContext
{
    /// <summary>The module (graph node) this hook fired for.</summary>
    public required Node Module { get; init; }
}
