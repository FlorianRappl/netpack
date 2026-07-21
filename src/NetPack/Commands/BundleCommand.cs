namespace NetPack.Commands;

using System.Diagnostics;
using CommandLine;
using NetPack.Graph;
using NetPack.Graph.Writers;

[Verb("bundle", HelpText = "Bundles the code starting at the given entry point.")]
public class BundleCommand : ICommand
{
    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string FilePath { get; set; } = "";

    [Option("outdir", Default = "dist", HelpText = "The directory where the generated files should be placed.")]
    public string OutDir { get; set; } = "dist";

    [Option("minify", Default = false, HelpText = "Indicates if the generated files should be optimized for file size.")]
    public bool Minify { get; set; } = false;

    [Option("sourcemap", Default = false, HelpText = "Emit a source map (.js.map) next to each JavaScript bundle.")]
    public bool SourceMap { get; set; } = false;

    [Option("clean", Default = false, HelpText = "Indicates if the output directory should be cleaned first.")]
    public bool Clean { get; set; } = false;

    [Option("external", HelpText = "Indicates if an import should be treated as an external.")]
    public IEnumerable<string> Externals { get; set; } = [];

    [Option("shared", HelpText = "Indicates if a dependency should be shared.")]
    public IEnumerable<string> Shared { get; set; } = [];

    [Option("format", Default = "esm", HelpText = "The output module format (esm, cjs, umd, systemjs). Currently only esm is implemented.")]
    public string Format { get; set; } = "esm";

    private static ModuleFormat ParseFormat(string format) => format.ToLowerInvariant() switch
    {
        "esm" or "es" or "module" => ModuleFormat.Esm,
        "cjs" or "commonjs" => ModuleFormat.CommonJs,
        "umd" => ModuleFormat.Umd,
        "system" or "systemjs" => ModuleFormat.SystemJs,
        _ => throw new InvalidOperationException($"Unknown output format '{format}'. Available: esm, cjs, umd, systemjs."),
    };

    public async Task Run()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            throw new InvalidOperationException("You must specify an entry point.");
        }
        
        if (string.IsNullOrEmpty(OutDir))
        {
            throw new InvalidOperationException("You must specify a non-empty target directory.");
        }

        var file = Path.Combine(Environment.CurrentDirectory, FilePath);
        var outdir = Path.Combine(Environment.CurrentDirectory, OutDir);
        var watch = Stopwatch.StartNew();

        Console.WriteLine("[netpack] Bundling '{0}' ...", FilePath);
        using var graph = await Traverse.From(file, Externals, Shared);
        var result = new DiskResultWriter(graph.Context, outdir);
        var options = new OutputOptions
        {
            IsOptimizing = Minify,
            IsReloading = false,
            WithSourceMaps = SourceMap,
            Format = ParseFormat(Format),
        };

        if (Clean && Directory.Exists(outdir))
        {
            Directory.Delete(outdir, true);
        }

        Directory.CreateDirectory(outdir);
        var emitted = await result.WriteOut(options);
        watch.Stop();

        PrintSummary(emitted, OutDir, watch.ElapsedMilliseconds, Minify, SourceMap);
    }

    private void PrintSummary(IReadOnlyList<EmittedFile> files, string outDir, long elapsedMs, bool minified, bool sourceMaps)
    {
        if (files.Count == 0)
        {
            Console.WriteLine("[netpack] Nothing was emitted.");
            return;
        }

        var nameWidth = Math.Max(files.Max(f => f.Name.Length), "total".Length);
        var sizeStrings = files.ToDictionary(f => f.Name, f => SizeFormatter.Human(f.Size));
        var totalHuman = SizeFormatter.Human(files.Sum(f => f.Size));
        var sizeWidth = Math.Max(sizeStrings.Values.Max(s => s.Length), totalHuman.Length);

        Console.WriteLine();
        Console.WriteLine("[netpack] Emitted {0} file{1} to '{2}' in {3} ms (minify: {4}, sourcemap: {5}):",
            files.Count, files.Count == 1 ? "" : "s", outDir, elapsedMs,
            minified ? "on" : "off", sourceMaps ? "on" : "off");
        Console.WriteLine();

        foreach (var f in files)
        {
            var modules = f.IsBundle
                ? $"   {f.Modules} module{(f.Modules == 1 ? "" : "s")}"
                : "";
            Console.WriteLine("  {0}   {1}{2}",
                f.Name.PadRight(nameWidth),
                sizeStrings[f.Name].PadLeft(sizeWidth),
                modules);
        }

        Console.WriteLine("  {0}   {1}", new string('-', nameWidth), new string('-', sizeWidth));
        Console.WriteLine("  {0}   {1}", "total".PadRight(nameWidth), totalHuman.PadLeft(sizeWidth));
        Console.WriteLine();
    }
}
