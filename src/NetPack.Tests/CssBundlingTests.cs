namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

/// <summary>
/// CSS bundling: <c>url()</c> references to local assets are rewritten to the
/// emitted (hashed) file name and honour the public path, while absolute URLs are
/// left untouched.
/// </summary>
public class CssBundlingTests
{
    private static async Task<string> BundleCss(OutputOptions options, Action<string> setup)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssb-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head><link rel=\"stylesheet\" href=\"./app.css\"></head><body></body></html>");
            setup(dir);

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            var css = graph.Context.Bundles.Values.OfType<CssBundle>().First();
            using var stream = await css.CreateStream(options);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static OutputOptions Options(string publicPath = "")
        => new() { IsOptimizing = false, IsReloading = false, PublicPath = publicPath };

    [Fact]
    public async Task Rewrites_a_local_asset_url_to_the_emitted_file()
    {
        var output = await BundleCss(Options(), dir =>
        {
            File.WriteAllText(Path.Combine(dir, "app.css"), ".a { background: url(./logo.png); }");
            File.WriteAllBytes(Path.Combine(dir, "logo.png"), new byte[] { 1, 2, 3, 4 });
        });

        // The reference is rewritten to the emitted, content-hashed file name
        // (logo.<hash>.png), so the original unhashed name no longer appears.
        Assert.Contains("logo.", output);
        Assert.DoesNotContain("logo.png", output);
    }

    [Fact]
    public async Task Leaves_absolute_urls_untouched()
    {
        var output = await BundleCss(Options(), dir =>
            File.WriteAllText(Path.Combine(dir, "app.css"), ".b { background: url(https://cdn.example.com/x.png); }"));

        Assert.Contains("https://cdn.example.com/x.png", output);
    }

    [Fact]
    public async Task Applies_public_path_to_asset_urls()
    {
        var output = await BundleCss(Options(publicPath: "https://cdn.test/assets"), dir =>
        {
            File.WriteAllText(Path.Combine(dir, "app.css"), ".a { background: url(./logo.png); }");
            File.WriteAllBytes(Path.Combine(dir, "logo.png"), new byte[] { 5, 6, 7, 8 });
        });

        Assert.Contains("https://cdn.test/assets/logo.", output);
    }
}
