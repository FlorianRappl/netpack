namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using NetPack.Graph;

/// <summary>
/// Context for compilation-level hooks — adds the <see cref="BundlerContext"/>
/// (fragments, bundles, assets) to the compiler-level state.
/// </summary>
public class CompilationContext : CompilerContext
{
    /// <summary>The bundler context with all fragments, bundles, and assets.</summary>
    public required BundlerContext BundlerContext { get; init; }
}
