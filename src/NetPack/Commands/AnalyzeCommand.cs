namespace NetPack.Commands;

using System.Reflection;
using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using NetPack.Graph;
using NetPack.Graph.Writers;
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
    
    private async Task<Metadata> Compile()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var options = new OutputOptions
        {
            IsOptimizing = true,
            IsReloading = false,
        };
        using var graph = await Traverse.From(file, Externals, Shared);
        var compilation = new MemoryResultWriter(graph.Context);
        await compilation.WriteOut(options);
        var results = new Metadata(graph, compilation);

        if (!string.IsNullOrEmpty(OutFile))
        {
            var path = Path.Combine(Environment.CurrentDirectory, OutFile);
            var text = results.Stringify();
            await File.WriteAllTextAsync(path, text);
        }

        return results;
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
