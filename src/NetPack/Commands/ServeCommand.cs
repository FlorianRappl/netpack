namespace NetPack.Commands;

using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Server;

[Verb("serve", HelpText = "Serves the bundled code starting at the given entry point.")]
public class ServeCommand : ICommand
{
    private readonly FileExtensionContentTypeProvider provider = new();

    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string FilePath { get; set; } = "";

    [Option("port", Default = 1234, HelpText = "The port where the development server should be running.")]
    public int Port { get; set; } = 1234;

    [Option("minify", Default = false, HelpText = "Indicates if the generated files should be optimized for file size.")]
    public bool Minify { get; set; } = false;

    [Option("external", HelpText = "Indicates if an import should be treated as an external.")]
    public IEnumerable<string> Externals { get; set; } = [];

    [Option("shared", HelpText = "Indicates if a dependency should be shared.")]
    public IEnumerable<string> Shared { get; set; } = [];

    private async Task<MemoryResultWriter> Compile()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath);
        Console.WriteLine("[netpack] Starting build ...");
        var graph = await Traverse.From(file, Externals, Shared);
        var compilation = new MemoryResultWriter(graph.Context);
        var options = new OutputOptions
        {
            IsOptimizing = Minify,
            IsReloading = true,
        };
        await compilation.WriteOut(options);
        Console.WriteLine("[netpack] Everything bundled!");
        return compilation;
    }

    public async Task Run()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            throw new InvalidOperationException("You must specify an entry point.");
        }

        using var watcher = new FileWatcher<MemoryResultWriter>(await Compile());

        var address = $"http://localhost:{Port}";
        var app = LiveServer.Create(address, watcher);

        app.MapGet("/", () =>
        {
            var content = watcher.Result.GetFile("index.html");
            
            if (content is null)
            {
                // 404
                return Results.NotFound();
            }
            
            return Results.Bytes(content, "text/html");
        });

        app.MapGet("/{*file}", (string file) =>
        {
            var original = watcher.Result.GetFile(file);
            var content = original ?? watcher.Result.GetFile("index.html");

            if (content is null)
            {
                // 404
                return Results.NotFound();
            }

            var contentType = original is not null ? GetMimeType(file) : "text/html";
            return Results.Bytes(content, contentType);
        });

        watcher.Install(Compile);

        Console.WriteLine("[netpack] DevServer running at {0}", address);
        await Task.Run(() => app.RunAsync());
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
