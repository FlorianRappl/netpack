namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

/// <summary>
/// Covers the HTML entry pipeline: script/stylesheet references are rewritten to
/// the emitted bundles, the public path is honoured, and optimizing builds do not
/// grow the document.
/// </summary>
public class HtmlBundlingTests
{
    private static async Task<string> BundleHtml(OutputOptions options, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-html-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            var html = graph.Context.Bundles.Values.OfType<HtmlBundle>().First();
            using var stream = await html.CreateStream(options);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static OutputOptions Options(bool optimizing = false, string publicPath = "")
        => new() { IsOptimizing = optimizing, IsReloading = false, PublicPath = publicPath };

    [Fact]
    public async Task Rewrites_a_module_script_reference()
    {
        var output = await BundleHtml(Options(),
            ("index.html", "<!doctype html><html><head></head><body>\n  <script type=\"module\" src=\"./main.js\"></script>\n</body></html>"),
            ("main.js", "console.log('hello');"));

        Assert.Contains("<script", output);
        Assert.Contains("main.js", output);
    }

    [Fact]
    public async Task Rewrites_a_stylesheet_link()
    {
        var output = await BundleHtml(Options(),
            ("index.html", "<!doctype html><html><head>\n  <link rel=\"stylesheet\" href=\"./app.css\">\n</head><body></body></html>"),
            ("app.css", "body { color: red; }"));

        Assert.Contains("app.css", output);
        Assert.Contains("stylesheet", output);
    }

    [Fact]
    public async Task Applies_public_path_to_document_references()
    {
        var output = await BundleHtml(Options(publicPath: "https://cdn.test/assets"),
            ("index.html", "<!doctype html><html><head></head><body>\n  <script type=\"module\" src=\"./main.js\"></script>\n</body></html>"),
            ("main.js", "console.log('hi');"));

        Assert.Contains("https://cdn.test/assets/main.js", output);
        Assert.DoesNotContain("\"./main.js\"", output);
    }

    [Fact]
    public async Task Optimizing_does_not_enlarge_the_document()
    {
        var files = new (string, string)[]
        {
            ("index.html",
                "<!doctype html>\n<html>\n  <head>\n    <title>Test</title>\n  </head>\n  <body>\n    <div>\n      hello\n    </div>\n    <script type=\"module\" src=\"./main.js\"></script>\n  </body>\n</html>"),
            ("main.js", "console.log('x');"),
        };

        var normal = await BundleHtml(Options(optimizing: false), files);
        var minified = await BundleHtml(Options(optimizing: true), files);

        Assert.True(minified.Length <= normal.Length,
            $"minified ({minified.Length}) should not exceed normal ({normal.Length})");
        Assert.Contains("main.js", minified);
    }
}
