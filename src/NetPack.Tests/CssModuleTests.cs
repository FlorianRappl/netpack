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

    // -- CSS Code Splitting ------------------------------------------------

    [Fact]
    public async Task Shared_css_across_multiple_scripts_in_html_is_extracted()
    {
        // An HTML entry with two scripts that both import the same CSS
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-split-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "shared.css"), ".common { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app1.js"), "import './shared.css';\nexport const a = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app2.js"), "import './shared.css';\nexport const b = 2;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));

            // Check that a shared CSS bundle was created
            var sharedCssBundles = graph.Context.Bundles.Values
                .OfType<CssBundle>()
                .Where(b => b.IsShared)
                .ToList();

            Assert.NotEmpty(sharedCssBundles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Non_shared_css_stays_inlined_in_js()
    {
        // Single entry point with CSS import - should be inlined
        var output = await Bundle("app.js",
            ("s.css", ".unique { color: green; }"),
            ("app.js", "import './s.css';\nexport const x = 1;"));

        // CSS is inlined as a virtual JS module
        Assert.Contains("document.createElement(\"style\")", output);
        Assert.Contains(".unique", output);
    }

    [Fact]
    public async Task Css_chunk_splitter_identifies_shared_css()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-splitter-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "shared.css"), ".common { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app1.js"), "import './shared.css';\nexport const a = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app2.js"), "import './shared.css';\nexport const b = 2;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));

            // The shared CSS should not be inlined in JS - it's extracted to a separate chunk
            var jsBundles = graph.Context.Bundles.Values.OfType<JsBundle>().ToList();
            foreach (var jsBundle in jsBundles)
            {
                var jsContent = jsBundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
                // Primary JS bundles should not contain the shared CSS content
                // (it's extracted to a separate CSS chunk)
                Assert.DoesNotContain("color:red", jsContent);
            }

            // The shared CSS should be in a CSS bundle
            var cssBundles = graph.Context.Bundles.Values.OfType<CssBundle>().ToList();
            Assert.NotEmpty(cssBundles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
