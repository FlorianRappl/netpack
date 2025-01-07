namespace NetPack.Commands;

using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPack.Graph;
using NetPack.Graph.Writers;

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

    private async Task<MemoryResultWriter> Compile()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath);
        var graph = await Traverse.From(file, Externals);
        var compilation = new MemoryResultWriter(graph.Context);
        compilation.Started += OnStarted;
        compilation.Finished += OnFinished;
        return compilation;
    }

    public async Task Run()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            throw new InvalidOperationException("You must specify an entry point.");
        }

        using var watcher = new FileSystemWatcher(Environment.CurrentDirectory)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };

        var compilation = await Compile();
        var latest = Task.CompletedTask;
        var current = Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        var address = $"http://localhost:{Port}";
        var options = new OutputOptions
        {
            IsOptimizing = Minify,
            IsReloading = true,
        };

        void Restart(object sender, FileSystemEventArgs e)
        {
            if (latest.Status == TaskStatus.WaitingForActivation || latest.Status == TaskStatus.WaitingToRun)
            {
                return;
            }

            if (compilation.HasFile(e.FullPath))
            {
                async Task Recompile()
                {
                    var currentTcs = tcs;
                    var newCompilation = await Compile();
                    await newCompilation.WriteOut(options);

                    if (!currentTcs.Task.IsCompleted)
                    {
                        tcs = new TaskCompletionSource();
                        currentTcs.SetResult();
                        compilation = newCompilation;
                    }
                }

                if (current.IsCompleted)
                {
                    current = Recompile();
                }
                else
                {
                    latest = current;
                    current = current.ContinueWith(_ => Recompile());
                }
            }
        }

        var writeOutTask = compilation.WriteOut(options);

        watcher.Changed += Restart;
        watcher.Deleted += Restart;
        watcher.Renamed += Restart;

        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddSimpleConsole();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, SourceGenerationContext.Default);
        });
        
        builder.WebHost.UseUrls(address);
        builder.Services.AddSingleton<IHostLifetime, NoopConsoleLifetime>();
        
        var app = builder.Build();

        app.MapGet("/", async () =>
        {
            await writeOutTask;
            var content = compilation.GetFile("index.html");
            
            if (content is null)
            {
                // 404
                return Results.NotFound();
            }
            
            return Results.Bytes(content, "text/html");
        });

        app.MapGet("/netpack", async (HttpContext ctx, CancellationToken ct) =>
        {
            var cancel = new TaskCompletionSource();
            ct.Register(() => cancel.SetResult());
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            
            while (!ct.IsCancellationRequested)
            {
                await Task.WhenAny(tcs.Task, cancel.Task);
                await ctx.Response.WriteAsync("event: change\n", ct);
                await ctx.Response.WriteAsync("data: {}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        });

        app.MapGet("/{*file}", async (string file) =>
        {
            await writeOutTask;

            var original = compilation.GetFile(file);
            var content = original ?? compilation.GetFile("index.html");

            if (content is null)
            {
                // 404
                return Results.NotFound();
            }

            var contentType = original is not null ? GetMimeType(file) : "text/html";
            return Results.Bytes(content, contentType);
        });

        Console.WriteLine("[netpack] DevServer running at {0}", address);
        await Task.Run(() => app.RunAsync());
    }

    private static void OnStarted(object? sender, EventArgs e)
    {
        Console.WriteLine("[netpack] Starting build ...");
    }

    private static void OnFinished(object? sender, EventArgs e)
    {
        Console.WriteLine("[netpack] Everything bundled!");
    }

    private string GetMimeType(string name)
    {
        if (provider.TryGetContentType(name, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }

    sealed class NoopConsoleLifetime(ILogger<NoopConsoleLifetime> logger) : IHostLifetime, IDisposable
    {
        private readonly ILogger<NoopConsoleLifetime> _logger = logger;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            Console.CancelKeyPress += OnCancelKeyPressed;
            return Task.CompletedTask;
        }

        private void OnCancelKeyPressed(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Task.Run(() => Environment.Exit(0));
        }

        public void Dispose()
        {
            Console.CancelKeyPress -= OnCancelKeyPressed;
        }
    }
}
