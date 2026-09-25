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
/// The package.json <c>browser</c> object-map (web target only): a bare specifier
/// can be remapped to a browser-friendly module, or stubbed with <c>false</c> to an
/// empty module. Node builds leave these alone (the field is web-only).
/// </summary>
public class BrowserFieldTests
{
    [Fact]
    public async Task Browser_map_remaps_a_bare_specifier_to_a_shim()
    {
        var output = await Bundle(
            packageJson: "{ \"browser\": { \"nodeonly\": \"./shim.js\" } }",
            files: new[]
            {
                ("index.js", "import v from 'nodeonly';\nexport const x = v;"),
                ("shim.js", "export default 'SHIM_MARKER';"),
            });

        Assert.Contains("SHIM_MARKER", output);
    }

    [Fact]
    public async Task Browser_map_false_stubs_a_module_to_empty()
    {
        var output = await Bundle(
            packageJson: "{ \"browser\": { \"fs\": false } }",
            files: new[]
            {
                ("index.js", "import fs from 'fs';\nexport const t = typeof fs;"),
            });

        // Valid JS, and `fs` is not kept as an external import — it resolved to the
        // shared empty module instead.
        var reparsed = Parser.ParseModule(output, "out.js",
            new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
        Assert.Empty(reparsed.Diagnostics);
        Assert.DoesNotContain("\"fs\"", output);
        Assert.DoesNotContain("'fs'", output);
    }

    [Fact]
    public async Task Node_target_ignores_the_browser_map()
    {
        // On Node the browser field is not consulted, so `fs` stays a runtime
        // built-in (kept external as `node:fs`) rather than being stubbed.
        var output = await Bundle(
            packageJson: "{ \"browser\": { \"fs\": false } }",
            files: new[]
            {
                ("index.js", "import fs from 'fs';\nexport const t = typeof fs;"),
            },
            platform: Platform.Node);

        Assert.Contains("node:fs", output);
    }

    private static async Task<string> Bundle(string packageJson, (string Name, string Content)[] files, Platform platform = Platform.Web)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-browser-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), packageJson);
            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(
                Path.Combine(dir, "index.js"), Array.Empty<string>(), Array.Empty<string>(), platform: platform);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
