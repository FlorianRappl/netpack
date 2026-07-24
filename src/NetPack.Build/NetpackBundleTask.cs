namespace NetPack.Build;

using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetPack;
using NetPack.Graph; // ModuleFormat / Platform live here (re-exposed via BundleOptions)

/// <summary>
/// MSBuild task that bundles a web entry point with netpack and writes the output
/// to a directory (typically <c>wwwroot</c>) as part of the build. Configure it
/// through <c>Netpack*</c> MSBuild properties (see the package's build/.targets).
/// </summary>
public sealed class NetpackBundleTask : Task
{
    /// <summary>The entry file to bundle (an .html, .js/.ts, .jsx/.tsx or .css).</summary>
    [Required]
    public string Entry { get; set; } = "";

    /// <summary>The directory to write the emitted files to (created if missing).</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>Optimize output for size (minify + tree-shake). Default true.</summary>
    public bool Minify { get; set; } = true;

    /// <summary>Emit a source map next to each JavaScript bundle.</summary>
    public bool SourceMaps { get; set; }

    /// <summary>Output module format: esm (default), cjs, umd, systemjs.</summary>
    public string Format { get; set; } = "esm";

    /// <summary>Target runtime: web (default), node, deno.</summary>
    public string Platform { get; set; } = "web";

    /// <summary>Naming template with [name]/[hash], e.g. "[name]-[hash]".</summary>
    public string EntryNames { get; set; } = "[name]";

    /// <summary>Base path/URL prepended to references to emitted files.</summary>
    public string PublicPath { get; set; } = "";

    /// <summary>Import specifiers to keep external (not bundled).</summary>
    public string[] Externals { get; set; } = Array.Empty<string>();

    public override bool Execute()
    {
        try
        {
            // Register the pure-managed, cross-platform image processor before
            // bundling — no SkiaSharp / OS dependency (unlike the native CLI).
            ImageSharpAssetProcessor.Register();

            var options = new BundleOptions
            {
                Minify = Minify,
                SourceMaps = SourceMaps,
                Format = ParseFormat(Format),
                Platform = ParsePlatform(Platform),
                EntryNames = EntryNames,
                PublicPath = PublicPath,
                Externals = Externals,
            };

            Log.LogMessage(MessageImportance.High, "netpack: bundling '{0}' -> '{1}'", Entry, OutputDirectory);

            var emitted = Bundler
                .WriteToDirectoryAsync(Entry, OutputDirectory, options)
                .GetAwaiter()
                .GetResult();

            foreach (var file in emitted)
            {
                Log.LogMessage(MessageImportance.Normal, "netpack: emitted {0} ({1} bytes)", file.Name, file.Size);
            }

            Log.LogMessage(MessageImportance.High, "netpack: {0} file(s) written to '{1}'", emitted.Count, OutputDirectory);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: false);
            return false;
        }
    }

    private static ModuleFormat ParseFormat(string format) => format.ToLowerInvariant() switch
    {
        "esm" or "es" or "module" => ModuleFormat.Esm,
        "cjs" or "commonjs" => ModuleFormat.CommonJs,
        "umd" => ModuleFormat.Umd,
        "system" or "systemjs" => ModuleFormat.SystemJs,
        _ => throw new InvalidOperationException($"Unknown format '{format}'. Available: esm, cjs, umd, systemjs."),
    };

    // Fully qualified: the enum name collides with this task's `Platform` string
    // property.
    private static NetPack.Graph.Platform ParsePlatform(string platform) => platform.ToLowerInvariant() switch
    {
        "web" or "browser" => NetPack.Graph.Platform.Web,
        "node" => NetPack.Graph.Platform.Node,
        "deno" => NetPack.Graph.Platform.Deno,
        _ => throw new InvalidOperationException($"Unknown platform '{platform}'. Available: web, node, deno."),
    };
}
