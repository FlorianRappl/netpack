namespace NetPack.Commands;

using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[Verb("preview", HelpText = "Serves the production build locally.")]
public class PreviewCommand : ICommand
{
    private readonly FileExtensionContentTypeProvider provider = new();

    [Value(0, Default = "dist", HelpText = "The directory containing the production build.")]
    public string Directory { get; set; } = "dist";

    [Option("port", Default = 4173, HelpText = "The port to preview on.")]
    public int Port { get; set; } = 4173;

    [Option("host", Default = "localhost", HelpText = "The host to bind to.")]
    public string Host { get; set; } = "localhost";

    public Task Run()
    {
        var dir = Path.Combine(Environment.CurrentDirectory, Directory);

        if (!System.IO.Directory.Exists(dir))
        {
            throw new InvalidOperationException($"Directory '{Directory}' does not exist. Run 'netpack bundle' first.");
        }

        var address = $"http://{Host}:{Port}";
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddSimpleConsole();
        builder.WebHost.UseUrls(address);

        var app = builder.Build();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dir),
            ContentTypeProvider = provider,
        });

        // SPA fallback: serve index.html for non-file routes
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dir),
            ContentTypeProvider = provider,
        });

        Console.WriteLine();
        Console.WriteLine("[netpack] Preview server running:");
        Console.WriteLine("            Local:   {0}/", address);
        Console.WriteLine();
        Console.WriteLine("[netpack] Press Ctrl+C to stop.");

        return app.RunAsync();
    }
}
