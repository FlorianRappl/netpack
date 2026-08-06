namespace NetPack.Commands;

using System.Linq;
using System.Reflection;
using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Json;
using NetPack.Server;

[Verb("analyze", HelpText = "Analyzes the generated bundles.")]
public class AnalyzeCommand : ICommand
{
    private readonly FileExtensionContentTypeProvider provider = new();
    
    [Value(0, HelpText = "The entry point file where the bundler should start.", Required = true)]
    public string FilePath { get; set; } = "";

    [Option("outfile", HelpText = "The optional file where the inspection data should be stored as a JSON.")]
    public string? OutFile { get; set; }

    [Option("port", Default = 8080, HelpText = "The port where the server should be running in case of --interactive.")]
    public int Port { get; set; } = 8080;

    [Option("interactive", Default = false, HelpText = "Indicates if a server should be started to inspect the analyzer data.")]
    public bool IsInteractive { get; set; } = false;

    [Option("external", HelpText = "Indicates if an import should be treated as an external.")]
    public IEnumerable<string> Externals { get; set; } = [];

    [Option("shared", HelpText = "Indicates if a dependency should be shared.")]
    public IEnumerable<string> Shared { get; set; } = [];

    [Option("banner", Default = "", HelpText = "Text placed on top of the entry JS bundle, followed by a newline, e.g. --banner \"// (c) 2026 Acme\". Empty by default.")]
    public string Banner { get; set; } = "";

    [Option("licenses", Default = "skip", HelpText = "Third-party license handling: skip (default), preamble, json, or spdx.")]
    public string Licenses { get; set; } = "skip";

    [Option("inline-limit", Default = 0, HelpText = "Maximum size in bytes to inline assets as data URIs instead of emitting files (0 = disabled).")]
    public int InlineLimit { get; set; } = 0;

    [Option("audit", Default = true, HelpText = "Audit graph dependencies against known vulnerabilities (npm advisories) and include the findings in the output. Use --audit false to disable.")]
    public bool Audit { get; set; } = true;

    private async Task<Metadata> Compile()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var options = new OutputOptions
        {
            IsOptimizing = true,
            IsReloading = false,
            Banner = Banner,
            Licenses = BundleCommand.ParseLicenses(Licenses),
            InlineLimit = InlineLimit,
        };
        using var graph = await Traverse.From(file, Externals, Shared, hookModules: PresetArgs.Hooks);
        var compilation = new MemoryResultWriter(graph.Context);
        await compilation.WriteOut(options);

        var audit = Audit ? await RunAudit(graph.Context) : null;
        var results = new Metadata(graph, compilation, audit);

        if (!string.IsNullOrEmpty(OutFile))
        {
            var path = Path.Combine(Environment.CurrentDirectory, OutFile);
            var text = results.Stringify();
            await File.WriteAllTextAsync(path, text);
        }

        return results;
    }

    private static async Task<AuditReport> RunAudit(BundlerContext context)
    {
        Console.WriteLine("[netpack] Auditing dependencies ...");
        var report = await DependencyAudit.RunAsync(context);

        if (report.Error is { Length: > 0 } error)
        {
            Console.WriteLine("[netpack] Audit could not complete: {0}", error);
        }
        else if (report.Vulnerabilities is { Count: > 0 } vulnerabilities)
        {
            var bySeverity = report.Summary is { Count: > 0 }
                ? string.Join(", ", report.Summary.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} {kv.Key}"))
                : vulnerabilities.Count.ToString();
            Console.WriteLine("[netpack] Audit: {0} advisory(ies) across {1} package(s) — {2}.",
                vulnerabilities.Count, report.Checked, bySeverity);
        }
        else
        {
            Console.WriteLine("[netpack] Audit: no known vulnerabilities in {0} package(s).", report.Checked);
        }

        return report;
    }

    public async Task Run()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            throw new InvalidOperationException("You must specify an entry point.");
        }
        
        var assembly = GetType().GetTypeInfo().Assembly;
        var names = assembly.GetManifestResourceNames();

        IResult GetFile(string name)
        {
            var contentType = GetMimeType(name);
            var stream = assembly.GetManifestResourceStream($"NetPack.{name}");

            if (stream is not null)
            {
                return Results.Stream(stream, contentType);
            }

            return Results.NotFound();
        }

        Console.WriteLine("[netpack] Gathering bundle information ...");
        var results = await Compile();
        Console.WriteLine("[netpack] Everything done!");

        if (IsInteractive)
        {
            using var watcher = new FileWatcher<Metadata>(results);

            var address = $"http://localhost:{Port}";
            var app = LiveServer.Create(address, watcher);

            app.MapGet("/", () => GetFile("index.html"));
            app.MapGet("/meta", () => Results.Content(watcher.Result.Stringify(), "application/json"));
            app.MapGet("/{name}", (string name) => GetFile(name));
            
            watcher.Install(Compile);
            
            Console.WriteLine("[netpack] Analyzer server running at {0}", address);
            await Task.Run(() => app.RunAsync());
        }
        else if (string.IsNullOrEmpty(OutFile))
        {
            // in this case we just print to the console
            Console.WriteLine("[netpack] Metadata =");
            Console.WriteLine(results.Stringify());
        }
    }

    private string GetMimeType(string name)
    {
        if (provider.TryGetContentType(name, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }
}
