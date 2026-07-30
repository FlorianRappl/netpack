namespace NetPack.Commands;

using System.Diagnostics;
using System.Threading;
using CommandLine;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Server;

[Verb("bundle", HelpText = "Bundles the code starting at the given entry point.")]
public class BundleCommand : ICommand
{
    private readonly DirectoryListingCache _resolutionCache = new();

    // Kept alive during watch mode for warm rebuild cache hits.
    private readonly BuildCache _buildCache = new();
    private readonly CodegenCache _codegenCache = new();
    private readonly RenderCache _renderCache = new();
    private readonly PassContext _passContext = new();

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

    [Option("format", Default = "esm", HelpText = "The output module format (esm, cjs, umd, systemjs).")]
    public string Format { get; set; } = "esm";

    [Option("platform", Default = "web", HelpText = "The target runtime (web, node, deno). Decides which modules stay external as runtime built-ins.")]
    public string Platform { get; set; } = "web";

    [Option("target", HelpText = "Comma-separated list of target runtimes, e.g. --target web,node. When set, overrides --platform and emits to <outdir>/<target>/ subdirectories.")]
    public string? Target { get; set; }

    [Option("define", HelpText = "Replace a global identifier with a constant expression, e.g. --define process.env.NODE_ENV=\"production\".")]
    public IEnumerable<string> Define { get; set; } = [];

    [Option("alias", HelpText = "Rewrite an import specifier, e.g. --alias react=preact/compat or --alias @=./src.")]
    public IEnumerable<string> Alias { get; set; } = [];

    [Option("loader", HelpText = "Override how a file extension is handled, e.g. --loader .svg=text (js, jsx, ts, tsx, json, css, text, base64, dataurl, file, copy, empty).")]
    public IEnumerable<string> Loader { get; set; } = [];

    [Option("entry-names", Default = "[name]", HelpText = "Naming template for emitted bundles with [name]/[hash] placeholders, e.g. [name]-[hash] for cache-busting.")]
    public string EntryNames { get; set; } = "[name]";

    [Option("public-path", Default = "", HelpText = "Base path/URL prepended to references to emitted files, e.g. https://cdn.example.com/app or /static.")]
    public string PublicPath { get; set; } = "";

    [Option("banner", Default = "", HelpText = "Text placed on top of the entry JS bundle, followed by a newline, e.g. --banner \"// (c) 2026 Acme\". Empty by default.")]
    public string Banner { get; set; } = "";

    [Option("conditions", HelpText = "Extra package.json 'exports' conditions to honour, on top of the platform defaults (e.g. --conditions development).")]
    public IEnumerable<string> Conditions { get; set; } = [];

    [Option("packages", Default = "bundle", HelpText = "Set to 'external' to keep every node_modules import external instead of bundling it.")]
    public string Packages { get; set; } = "bundle";

    [Option("watch", Default = false, HelpText = "Rebuild and write to the output directory whenever a source file changes (no dev server).")]
    public bool Watch { get; set; } = false;

    [Option("watch-delay", Default = 200, HelpText = "Debounce delay in milliseconds for watch mode (default 200).")]
    public int WatchDelay { get; set; } = 200;

    [Option("clear-screen", Default = false, HelpText = "Clear the terminal on rebuild.")]
    public bool ClearScreen { get; set; } = false;

    [Option("preload", Default = true, HelpText = "Emit <link rel=\"modulepreload\"> for shared JS bundles (use --no-preload to disable).")]
    public bool Preload { get; set; } = true;

    [Option("inline-limit", Default = 0, HelpText = "Maximum size in bytes to inline assets as data URIs instead of emitting files (0 = disabled).")]
    public int InlineLimit { get; set; } = 0;

    private static bool ParsePackages(string packages) => packages.ToLowerInvariant() switch
    {
        "external" => true,
        "bundle" or "" => false,
        _ => throw new InvalidOperationException($"Unknown --packages '{packages}'. Available: bundle, external."),
    };

    /// <summary>Parses repeated <c>key=value</c> option entries (split on the
    /// first <c>=</c>) into a dictionary; later entries win on duplicate keys.</summary>
    internal static IReadOnlyDictionary<string, string> ParseKeyValues(IEnumerable<string> entries, string optionName)
    {
        var map = new Dictionary<string, string>();

        foreach (var entry in entries)
        {
            var index = entry.IndexOf('=');

            if (index <= 0)
            {
                throw new InvalidOperationException($"Invalid --{optionName} '{entry}'. Expected key=value.");
            }

            map[entry[..index]] = entry[(index + 1)..];
        }

        return map;
    }

    private static ModuleFormat ParseFormat(string format) => format.ToLowerInvariant() switch
    {
        "esm" or "es" or "module" => ModuleFormat.Esm,
        "cjs" or "commonjs" => ModuleFormat.CommonJs,
        "umd" => ModuleFormat.Umd,
        "system" or "systemjs" => ModuleFormat.SystemJs,
        _ => throw new InvalidOperationException($"Unknown output format '{format}'. Available: esm, cjs, umd, systemjs."),
    };

    private static NetPack.Graph.Platform ParsePlatform(string platform) => platform.ToLowerInvariant() switch
    {
        "web" or "browser" => NetPack.Graph.Platform.Web,
        "node" => NetPack.Graph.Platform.Node,
        "deno" => NetPack.Graph.Platform.Deno,
        _ => throw new InvalidOperationException($"Unknown platform '{platform}'. Available: web, node, deno."),
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

        var defines = ParseKeyValues(Define, "define");
        var aliases = ParseKeyValues(Alias, "alias");
        var loaders = ParseKeyValues(Loader, "loader");
        var externalPackages = ParsePackages(Packages);

        // Load .env files and merge with --define overrides
        var envDefines = EnvLoader.LoadFromDirectory(Environment.CurrentDirectory);
        var mergedDefines = new Dictionary<string, string>(envDefines);

        // --define overrides .env values
        foreach (var (key, value) in defines)
        {
            mergedDefines[key] = value;
        }

        var options = new OutputOptions
        {
            IsOptimizing = Minify,
            IsReloading = false,
            WithSourceMaps = SourceMap,
            Format = ParseFormat(Format),
            EntryNames = EntryNames,
            PublicPath = PublicPath,
            Banner = Banner,
            EnableModulePreload = Preload,
            InlineLimit = InlineLimit,
        };

        // Resolve targets: --target overrides --platform.
        var targets = ParseTargets();

        if (targets.Count > 1)
        {
            Console.WriteLine("[netpack] Multi-target build: {0}", string.Join(", ", targets.Select(t => t.ToString().ToLowerInvariant())));

            foreach (var target in targets)
            {
                Console.WriteLine();
                var targetOutDir = Path.Combine(outdir, target.ToString().ToLowerInvariant());
                Directory.CreateDirectory(targetOutDir);

                await BuildOnce(file, targetOutDir, options, mergedDefines, aliases, loaders, externalPackages, target);
            }

            Console.WriteLine();
            Console.WriteLine("[netpack] Multi-target build complete ({0} targets):", targets.Count);
            foreach (var target in targets)
            {
                var dir = Path.Combine(OutDir, target.ToString().ToLowerInvariant());
                Console.WriteLine("  {0,-6} → {1}", target.ToString().ToLowerInvariant() + ":", dir);
            }
        }
        else
        {
            var target = targets.First();

            if (Clean && Directory.Exists(outdir))
            {
                Directory.Delete(outdir, true);
            }

            Directory.CreateDirectory(outdir);

            var writer = await BuildOnce(file, outdir, options, mergedDefines, aliases, loaders, externalPackages, target);

            if (Watch)
            {
                using var watcher = new FileWatcher<DiskResultWriter>(writer, WatchDelay, invalidateDirectory: _resolutionCache.Invalidate);
                watcher.Install(() =>
                {
                    if (ClearScreen)
                    {
                        Console.Clear();
                    }

                    return BuildOnce(file, outdir, options, mergedDefines, aliases, loaders, externalPackages, target);
                });
                Console.WriteLine();
                Console.WriteLine("[netpack] Watching for changes — press Ctrl+C to stop.");
                await Task.Delay(Timeout.Infinite);
            }
        }
    }

    private List<NetPack.Graph.Platform> ParseTargets()
    {
        if (!string.IsNullOrWhiteSpace(Target))
        {
            return Target.Split(',')
                .Select(s => s.Trim().ToLowerInvariant())
                .Select(ParsePlatform)
                .ToList();
        }

        return [ParsePlatform(Platform)];
    }

    private async Task<DiskResultWriter> BuildOnce(
        string file, string outdir, OutputOptions options,
        IReadOnlyDictionary<string, string> defines, IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, string> loaders, bool externalPackages, NetPack.Graph.Platform platform)
    {
        var watch = Stopwatch.StartNew();
        Console.WriteLine("[netpack] Bundling '{0}' ({1}) ...", FilePath, platform.ToString().ToLowerInvariant());
        using var graph = await Traverse.From(
            file, Externals, Shared, platform: platform,
            defines: defines, aliases: aliases, loaders: loaders,
            conditions: Conditions, externalPackages: externalPackages, directoryFiles: Watch ? _resolutionCache : null,
            buildCache: _buildCache, codegenCache: _codegenCache, renderCache: _renderCache, passContext: _passContext);
        var result = new DiskResultWriter(graph.Context, outdir);
        var emitted = await result.WriteOut(options);
        watch.Stop();

        PrintSummary(emitted, OutDir, watch.ElapsedMilliseconds, Minify, SourceMap);
        return result;
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
