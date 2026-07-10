namespace NetPack.Commands;

using System.Text;
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
    private const string ReloadMessage = "event: reload\ndata: {}\n\n";

    private readonly FileExtensionContentTypeProvider provider = new();

    // Kept alive across recompiles so module ids stay stable — a precondition
    // for addressing an already loaded module during a hot update.
    private readonly ModuleIdMap _moduleIds = new();

    // The previous compile's per-module factory sources, used to diff.
    private Dictionary<int, string> _factories = new();

    // The current server-sent-event payload consumed by the browser client.
    private volatile string _hmrMessage = ReloadMessage;

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

    private async Task<(MemoryResultWriter Writer, Dictionary<int, string> Factories)> Compile()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath);
        Console.WriteLine("[netpack] Starting build ...");
        using var graph = await Traverse.From(file, Externals, Shared, _moduleIds);
        var compilation = new MemoryResultWriter(graph.Context);
        var options = new OutputOptions
        {
            IsOptimizing = Minify,
            IsReloading = true,
            WithSourceMaps = true,
        };
        await compilation.WriteOut(options);
        var factories = new Dictionary<int, string>(graph.Context.ModuleFactories);
        Console.WriteLine("[netpack] Everything bundled!");
        return (compilation, factories);
    }

    /// <summary>
    /// Diffs the freshly compiled module factories against the previous compile
    /// and produces the SSE payload: a granular <c>update</c> when only module
    /// bodies changed, otherwise a full <c>reload</c> (a module was added/removed
    /// or a non-JS asset changed).
    /// </summary>
    private string ComputeMessage(Dictionary<int, string> factories)
    {
        var updates = new List<KeyValuePair<int, string>>();
        foreach (var entry in factories)
        {
            if (!_factories.TryGetValue(entry.Key, out var previous) || previous != entry.Value)
            {
                updates.Add(entry);
            }
        }

        var removed = false;
        foreach (var id in _factories.Keys)
        {
            if (!factories.ContainsKey(id))
            {
                removed = true;
                break;
            }
        }

        _factories = factories;

        if (removed || updates.Count == 0)
        {
            return ReloadMessage;
        }

        var sb = new StringBuilder();
        sb.Append("event: update\ndata: {\"m\":[");
        for (var i = 0; i < updates.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"i\":").Append(updates[i].Key).Append(",\"c\":\"").Append(JsonEscape(updates[i].Value)).Append("\"}");
        }
        sb.Append("]}\n\n");
        return sb.ToString();
    }

    private static string JsonEscape(string value)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    public async Task Run()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            throw new InvalidOperationException("You must specify an entry point.");
        }

        var initial = await Compile();
        _factories = initial.Factories;
        using var watcher = new FileWatcher<MemoryResultWriter>(initial.Writer);

        var address = $"http://localhost:{Port}";
        var app = LiveServer.Create(address, watcher, () => _hmrMessage);

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

        watcher.Install(async () =>
        {
            var result = await Compile();
            _hmrMessage = ComputeMessage(result.Factories);
            return result.Writer;
        });

        // Print the real, clickable URL once the server is actually listening —
        // app.Urls is only populated after the host has bound its addresses.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            IEnumerable<string> urls = app.Urls.Count > 0 ? app.Urls : new[] { address };

            Console.WriteLine();
            Console.WriteLine("[netpack] Dev server running:");

            foreach (var url in urls)
            {
                // Normalize wildcard binds to a link the user can actually click.
                var shown = url
                    .Replace("http://0.0.0.0", "http://localhost")
                    .Replace("http://[::]", "http://localhost")
                    .Replace("https://0.0.0.0", "https://localhost")
                    .Replace("https://[::]", "https://localhost")
                    .TrimEnd('/');
                Console.WriteLine("            Local:   {0}/", shown);
            }

            Console.WriteLine();
            Console.WriteLine("[netpack] Watching for changes — press Ctrl+C to stop.");
        });

        await Task.Run(() => app.RunAsync());
    }

    private string GetMimeType(string name)
    {
        if (name.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        {
            return "application/json";
        }

        if (provider.TryGetContentType(name, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }
}
