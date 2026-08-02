namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using NetPack.Graph;

/// <summary>
/// Context for the per-module build hooks — adds the specific module being built.
/// </summary>
public class ModuleBuildContext : CompilationContext
{
    /// <summary>The module (graph node) this hook fired for.</summary>
    public required Node Module { get; init; }
}
