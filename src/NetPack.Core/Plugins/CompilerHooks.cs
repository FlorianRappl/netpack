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
