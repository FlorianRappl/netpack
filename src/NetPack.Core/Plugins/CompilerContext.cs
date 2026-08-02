namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using NetPack.Graph;

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
