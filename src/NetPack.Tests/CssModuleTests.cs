namespace NetPack.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

public class CssModuleTests
{
    // -- GenerateModule (virtual JS module) --------------------------------

    [Fact]
    public void Generates_named_exports_for_identifier_safe_classes()
    {
        var map = new Dictionary<string, string> { ["title"] = "title_abc123" };
        var js = CssModules.GenerateModule(".title_abc123{color:red}", map);

        Assert.Contains("export const title = \"title_abc123\"", js);
        Assert.Contains("export default {", js);
        Assert.Contains("\"title\": \"title_abc123\"", js);
    }

    [Fact]
    public void Hyphenated_classes_only_appear_in_default_map()
    {
        var map = new Dictionary<string, string> { ["big-text"] = "big-text_abc123" };
        var js = CssModules.GenerateModule(".big-text_abc123{}", map);

        // `big-text` is not a valid identifier, so no named export…
        Assert.DoesNotContain("export const big-text", js);
        // …but it is reachable through the default map.
        Assert.Contains("\"big-text\": \"big-text_abc123\"", js);
    }

    [Fact]
    public void Injects_a_style_element_at_runtime()
    {
        var js = CssModules.GenerateModule(".a_x{color:blue}", new Dictionary<string, string> { ["a"] = "a_x" });

        Assert.Contains("document.createElement(\"style\")", js);
        Assert.Contains("document.head.appendChild", js);
        Assert.Contains(".a_x", js); // the CSS text is embedded
    }

    // -- End-to-end through the bundler ------------------------------------

    [Fact]
    public async Task Named_css_import_hashes_classes_and_maps_them()
    {
        var output = await Bundle("app.js",
            ("s.css", ".title { color: red; }\n.subtitle { color: blue; }"),
            ("app.js", "import { title } from './s.css';\nexport const t = title;"));

        // The class name is hashed in both the exported string and the embedded CSS…
        Assert.Contains("title_", output);
        Assert.Contains(".title_", output); // hashed selector inside the injected CSS
        // …and injected as a runtime <style>.
        Assert.Contains("createElement(\"style\")", output);
    }

    [Fact]
    public async Task Hashes_are_stable_across_compiles()
    {
        var files = new[]
        {
            ("s.css", ".box { color: green; }"),
            ("app.js", "import { box } from './s.css';\nexport const b = box;"),
        };

        var first = await Bundle("app.js", files);
        var second = await Bundle("app.js", files);

        var hashFirst = ExtractHash(first, "box_");
        var hashSecond = ExtractHash(second, "box_");
        Assert.Equal(hashFirst, hashSecond);
    }

    private static string ExtractHash(string output, string prefix)
    {
        var idx = output.IndexOf(prefix, System.StringComparison.Ordinal);
        Assert.True(idx >= 0, $"expected '{prefix}' in output");
        var start = idx + prefix.Length;
        var end = start;
        while (end < output.Length && Uri.IsHexDigit(output[end])) end++;
        return output[start..end];
    }

    private static async Task<string> Bundle(string entry, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            if (!files.Any(f => f.Name == "package.json"))
            {
                await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            }

            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, entry));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
