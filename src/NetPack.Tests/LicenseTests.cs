namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Graph.Writers;
using Xunit;

public class LicenseTests
{
    // -- comment extraction ------------------------------------------------

    [Fact]
    public void Extracts_only_legal_comments()
    {
        var source =
            "/*! preserved bang */\n" +
            "/* ordinary block */\n" +
            "// ordinary line\n" +
            "//! preserved line\n" +
            "/** @license MIT */\n" +
            "/* @preserve keep */\n" +
            "const s = \"/* not a comment */ @license\";\n" +
            "export const x = 1;";

        var comments = LicenseCollector.ExtractLegalComments(source);

        Assert.Contains("/*! preserved bang */", comments);
        Assert.Contains("//! preserved line", comments);
        Assert.Contains("/** @license MIT */", comments);
        Assert.Contains("/* @preserve keep */", comments);

        Assert.DoesNotContain("/* ordinary block */", comments);
        // Comment-looking text inside a string literal is ignored.
        Assert.DoesNotContain(comments, c => c.Contains("not a comment"));
    }

    // -- preamble ----------------------------------------------------------

    [Fact]
    public async Task Preamble_puts_legal_comments_in_the_bundle_head()
    {
        var output = await Bundle(LicenseMode.Preamble,
            "/*! (c) 2026 Acme — MIT */\nexport const x = 1;");

        Assert.Contains("/*! (c) 2026 Acme — MIT */", output);
    }

    [Fact]
    public async Task Preamble_follows_the_banner()
    {
        var output = await Bundle(LicenseMode.Preamble,
            "/*! license here */\nexport const x = 1;",
            banner: "// my banner");

        var bannerAt = output.IndexOf("// my banner", StringComparison.Ordinal);
        var licenseAt = output.IndexOf("/*! license here */", StringComparison.Ordinal);

        Assert.True(bannerAt >= 0 && licenseAt > bannerAt, "License preamble should come after the banner.");
    }

    [Fact]
    public async Task Skip_omits_legal_comments()
    {
        var output = await Bundle(LicenseMode.Skip,
            "/*! (c) 2026 Acme */\nexport const x = 1;");

        Assert.DoesNotContain("2026 Acme", output);
    }

    // -- manifest files ----------------------------------------------------

    [Theory]
    [InlineData(LicenseMode.Json, "licenses.json")]
    [InlineData(LicenseMode.Spdx, "licenses.spdx")]
    public async Task Manifest_modes_emit_a_license_file(LicenseMode mode, string expected)
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export const x = 1;");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var writer = new MemoryResultWriter(graph.Context);
            var emitted = await writer.WriteOut(new OutputOptions
            {
                IsOptimizing = false,
                IsReloading = false,
                Licenses = mode,
            });

            Assert.Contains(emitted, f => f.Name == expected);
            Assert.NotNull(writer.GetFile(expected));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Spdx_render_produces_a_valid_header()
    {
        var context = new BundlerContext("/root", FeatureFlags.None);
        var spdx = LicenseCollector.Render(LicenseMode.Spdx, context);

        Assert.StartsWith("SPDXVersion: SPDX-2.3", spdx);
        Assert.Contains("Creator: Tool: netpack", spdx);
    }

    // -- helpers -----------------------------------------------------------

    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-lic-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<string> Bundle(LicenseMode licenses, string main, string banner = "")
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), main);

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

            return bundle.Stringify(new OutputOptions
            {
                IsOptimizing = false,
                IsReloading = false,
                Banner = banner,
                Licenses = licenses,
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
