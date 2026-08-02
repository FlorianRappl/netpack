namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using NetPack.Graph;

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
