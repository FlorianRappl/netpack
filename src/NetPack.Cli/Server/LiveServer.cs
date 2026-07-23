namespace NetPack.Server;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPack.Json;

static class LiveServer
{
    public static WebApplication Create<T>(string address, FileWatcher<T> watcher, Func<string>? message = null)
        where T : IFileLocator
    {
        // Default behaviour (used by the analyzer): a plain change event that the
        // client turns into a full reload.
        message ??= static () => "event: change\ndata: {}\n\n";

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

        app.MapGet("/netpack", async (HttpContext ctx, CancellationToken ct) =>
        {
            var cancel = new TaskCompletionSource();
            ct.Register(() => cancel.SetResult());
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            
            while (!ct.IsCancellationRequested)
            {
                await Task.WhenAny(watcher.Next, cancel.Task);
                await ctx.Response.WriteAsync(message(), ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        });

        return app;
    }
}