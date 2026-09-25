namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// A genuinely dynamic <c>require(&lt;expr&gt;)</c> is left in place (constant ones
/// are bundled). On Node/Deno the runtime falls back to the real require for a
/// specifier that isn't a bundled module, so those requires still work; the web has
/// no such fallback.
/// </summary>
public class NodeRequireFallbackTests
{
    [Fact]
    public async Task Node_esm_dynamic_require_gets_createRequire_fallback()
    {
        var output = await Bundle(Platform.Node, ModuleFormat.Esm,
            ("main.js", "const name = globalThis.x;\nexport const m = require(name);"));

        Assert.Contains("createRequire", output);
        Assert.Contains("import.meta.url", output);
        Assert.Contains("in __m", output);   // the fallback guard
        AssertValid(output);
    }

    [Fact]
    public async Task Node_cjs_dynamic_require_uses_ambient_require()
    {
        var output = await Bundle(Platform.Node, ModuleFormat.CommonJs,
            ("main.js", "const name = globalThis.x;\nexport const m = require(name);"));

        Assert.Contains("in __m", output);
        Assert.Contains("return require(", output);
        Assert.DoesNotContain("createRequire", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Web_dynamic_require_has_no_fallback()
    {
        var output = await Bundle(Platform.Web, ModuleFormat.Esm,
            ("main.js", "const name = globalThis.x;\nexport const m = require(name);"));

        Assert.DoesNotContain("createRequire", output);
        Assert.DoesNotContain("in __m", output);
    }

    [Fact]
    public async Task Static_require_on_node_needs_no_fallback()
    {
        // A constant require is bundled, so there's no dynamic require to fall back
        // for — the createRequire shim must not appear.
        var output = await Bundle(Platform.Node, ModuleFormat.Esm,
            ("main.js", "const dep = require('./dep.js');\nexport const m = dep;"),
            ("dep.js", "module.exports = 'STATIC_DEP';"));

        Assert.Contains("STATIC_DEP", output);
        Assert.DoesNotContain("createRequire", output);
    }

    private static void AssertValid(string js)
        => Assert.Empty(Parser.ParseModule(js, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);

    private static async Task<string> Bundle(Platform platform, ModuleFormat format, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-noderequire-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), platform: platform);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false, Format = format });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
