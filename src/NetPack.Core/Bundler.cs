namespace NetPack;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Writers;

/// <summary>
/// Options for a bundle run. These mirror the CLI flags; every property has a
/// sensible default, so <c>new BundleOptions()</c> is a valid production-ESM build.
/// </summary>
public sealed record BundleOptions
{
    /// <summary>Optimize output for size (minify JS/CSS/HTML, tree-shake).</summary>
    public bool Minify { get; init; }

    /// <summary>Emit a source map next to each JavaScript bundle.</summary>
    public bool SourceMaps { get; init; }

    /// <summary>The output module format for JS bundles (default ESM).</summary>
    public ModuleFormat Format { get; init; } = ModuleFormat.Esm;

    /// <summary>The target runtime (default web).</summary>
    public Platform Platform { get; init; } = Platform.Web;

    /// <summary>Naming template for emitted bundles (<c>[name]</c>/<c>[hash]</c>).</summary>
    public string EntryNames { get; init; } = "[name]";

    /// <summary>Base path/URL prepended to references to emitted files.</summary>
    public string PublicPath { get; init; } = "";

    /// <summary>Import specifiers to keep external (not bundled).</summary>
    public IEnumerable<string> Externals { get; init; } = [];

    /// <summary>Dependencies emitted as shared bundles + import-map entries.</summary>
    public IEnumerable<string> Shared { get; init; } = [];

    /// <summary>Compile-time constant substitutions (<c>--define</c>).</summary>
    public IReadOnlyDictionary<string, string>? Define { get; init; }

    /// <summary>Import-specifier rewrites (<c>--alias</c>).</summary>
    public IReadOnlyDictionary<string, string>? Alias { get; init; }

    /// <summary>Per-extension loader overrides (<c>--loader</c>).</summary>
    public IReadOnlyDictionary<string, string>? Loader { get; init; }

    /// <summary>Extra <c>package.json</c> <c>exports</c> conditions.</summary>
    public IEnumerable<string>? Conditions { get; init; }

    /// <summary>Keep every bare (node_modules) import external.</summary>
    public bool ExternalPackages { get; init; }
}

/// <summary>The result of a bundle: emitted-file metadata plus each file's bytes.</summary>
public sealed record BundleResult(IReadOnlyList<EmittedFile> Files, IReadOnlyDictionary<string, byte[]> Outputs);

/// <summary>
/// The public entry point for using netpack as a library. Bundles a project
/// starting at an entry file (an <c>.html</c>, <c>.js</c>/<c>.ts</c>,
/// <c>.jsx</c>/<c>.tsx</c>, <c>.css</c>, …) either to memory
/// (<see cref="BundleAsync"/>) or straight to a directory
/// (<see cref="WriteToDirectoryAsync"/>).
/// </summary>
public static class Bundler
{
    private static OutputOptions ToOutputOptions(BundleOptions options) => new()
    {
        IsOptimizing = options.Minify,
        IsReloading = false,
        WithSourceMaps = options.SourceMaps,
        Format = options.Format,
        EntryNames = options.EntryNames,
        PublicPath = options.PublicPath,
    };

    private static Task<Traverse> BuildGraph(string entryPath, BundleOptions options) => Traverse.From(
        entryPath, options.Externals, options.Shared, platform: options.Platform,
        defines: options.Define, aliases: options.Alias, loaders: options.Loader,
        conditions: options.Conditions, externalPackages: options.ExternalPackages);

    /// <summary>Bundles the project and returns every emitted file in memory.</summary>
    public static async Task<BundleResult> BundleAsync(string entryPath, BundleOptions? options = null)
    {
        options ??= new BundleOptions();
        using var graph = await BuildGraph(entryPath, options);
        var writer = new MemoryResultWriter(graph.Context);
        var files = await writer.WriteOut(ToOutputOptions(options));
        var outputs = files.ToDictionary(file => file.Name, file => writer.GetFile(file.Name) ?? []);
        return new BundleResult(files, outputs);
    }

    /// <summary>Bundles the project and writes every emitted file to
    /// <paramref name="outputDirectory"/> (created if it does not exist),
    /// returning the emitted-file report.</summary>
    public static async Task<IReadOnlyList<EmittedFile>> WriteToDirectoryAsync(
        string entryPath, string outputDirectory, BundleOptions? options = null)
    {
        options ??= new BundleOptions();
        using var graph = await BuildGraph(entryPath, options);
        Directory.CreateDirectory(outputDirectory);
        var writer = new DiskResultWriter(graph.Context, outputDirectory);
        return await writer.WriteOut(ToOutputOptions(options));
    }
}
