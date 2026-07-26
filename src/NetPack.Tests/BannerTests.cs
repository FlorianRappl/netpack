namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

public class BannerTests
{
    private static JsBundle Primary(Traverse graph)
        => graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-banner-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Banner_is_prepended_to_entry_bundle()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export const x = 1;");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var output = Primary(graph).Stringify(new OutputOptions
            {
                IsOptimizing = false,
                IsReloading = false,
                Banner = "// My banner",
            });

            Assert.StartsWith("// My banner\n", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Empty_banner_is_discarded()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export const x = 1;");

            var options = new OutputOptions { IsOptimizing = false, IsReloading = false };

            // A fresh graph per stringify — the lowering mutates the AST in place.
            using var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var withoutBanner = Primary(g1).Stringify(options);

            using var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var withEmptyBanner = Primary(g2).Stringify(options with { Banner = "" });

            // No leading blank line, and identical to the no-banner output.
            Assert.False(withEmptyBanner.StartsWith("\n", StringComparison.Ordinal));
            Assert.Equal(withoutBanner, withEmptyBanner);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Banner_works_with_minify()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export const x = 1;");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var output = Primary(graph).Stringify(new OutputOptions
            {
                IsOptimizing = true,
                IsReloading = false,
                Banner = "/* minified */",
            });

            Assert.StartsWith("/* minified */\n", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Banner_only_applies_to_the_entry_not_shared_chunks()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "common.js"), "export const shared = 42;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app1.js"), "import { shared } from './common.js';\nexport const a = shared;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app2.js"), "import { shared } from './common.js';\nexport const b = shared;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            var options = new OutputOptions { IsOptimizing = false, IsReloading = false, Banner = "// header" };

            var jsBundles = graph.Context.Bundles.Values.OfType<JsBundle>().ToList();
            var shared = jsBundles.Where(b => b.IsShared).ToList();
            var entries = jsBundles.Where(b => !b.IsShared).ToList();

            Assert.NotEmpty(shared);
            Assert.NotEmpty(entries);

            // Shared split chunks never carry the banner.
            foreach (var bundle in shared)
            {
                Assert.DoesNotContain("// header", bundle.Stringify(options));
            }

            // Every entry bundle starts with the banner.
            foreach (var bundle in entries)
            {
                Assert.StartsWith("// header\n", bundle.Stringify(options));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Banner_shifts_source_map_by_its_line_count()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export const x = 1;\nexport const y = 2;");

            // A two-line banner adds two generated lines to the top of the bundle
            // (its single embedded newline plus the newline appended after it), so
            // every mapping shifts down by two lines — i.e. two extra ';' separators
            // are prepended to the encoded mappings.
            var options = new OutputOptions { IsOptimizing = false, IsReloading = false, WithSourceMaps = true };

            using var plain = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var plainBundle = Primary(plain);
            _ = plainBundle.Stringify(options);
            var plainMappings = Mappings(plainBundle.SourceMap!);

            using var withBanner = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var bannerBundle = Primary(withBanner);
            var output = bannerBundle.Stringify(options with { Banner = "// line 1\n// line 2" });
            var bannerMappings = Mappings(bannerBundle.SourceMap!);

            Assert.StartsWith("// line 1\n// line 2\n", output);
            Assert.Equal(";;" + plainMappings, bannerMappings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Extracts the raw <c>mappings</c> string from a source-map JSON. VLQ
    /// mappings only ever contain base64 chars plus <c>;</c>/<c>,</c>, so a simple
    /// scan to the next quote is safe.</summary>
    private static string Mappings(byte[] sourceMap)
    {
        var json = Encoding.UTF8.GetString(sourceMap);
        const string key = "\"mappings\":\"";
        var start = json.IndexOf(key, StringComparison.Ordinal) + key.Length;
        var end = json.IndexOf('"', start);
        return json[start..end];
    }
}
