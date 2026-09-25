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
/// On the web there is no <c>__dirname</c>/<c>__filename</c>; netpack defines them
/// per module so CommonJS packages that reference them don't throw. Node/Deno leave
/// them to the runtime.
/// </summary>
public class BrowserGlobalsTests
{
    [Fact]
    public async Task Web_defines_dirname_and_filename_when_referenced()
    {
        var output = await Bundle(Platform.Web, "export const d = __dirname;\nexport const f = __filename;");

        Assert.Contains("var __dirname", output);   // both are emitted in one `var` statement
        Assert.Contains("__filename", output);
        Assert.Contains("/main.js", output);         // __filename is the module's path
        AssertValid(output);
    }

    [Fact]
    public async Task Web_leaves_dirname_property_access_untouched()
    {
        // Only free identifier references are shimmed — an object property named
        // __dirname must survive as a property.
        var output = await Bundle(Platform.Web,
            "const o = { __dirname: 5 };\nexport const v = o.__dirname;\nexport const d = __dirname;");

        Assert.Contains("var __dirname", output);   // the shim
        Assert.Contains(".__dirname", output);      // the property access is intact
        AssertValid(output);
    }

    [Fact]
    public async Task Web_does_not_define_when_not_referenced()
    {
        var output = await Bundle(Platform.Web, "export const x = 1;");
        Assert.DoesNotContain("__dirname", output);
    }

    [Fact]
    public async Task Node_leaves_dirname_to_the_runtime()
    {
        var output = await Bundle(Platform.Node, "export const d = __dirname;");
        Assert.DoesNotContain("var __dirname", output);   // not shimmed on Node
    }

    private static void AssertValid(string js)
        => Assert.Empty(Parser.ParseModule(js, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);

    private static async Task<string> Bundle(Platform platform, string mainContent)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-globals-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), mainContent);

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), platform: platform);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
