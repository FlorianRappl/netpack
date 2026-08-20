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

    // -- Build-time split (--css link / none) ------------------------------

    [Theory]
    [InlineData(CssMode.None)]
    [InlineData(CssMode.Link)]
    public async Task Split_modes_emit_a_css_file_and_a_non_injecting_shim(CssMode mode)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-split2-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "s.css"), ".title { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"),
                "import { title } from './s.css';\nexport const t = title;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "app.js"), Array.Empty<string>(), Array.Empty<string>(), cssMode: mode);

            // A standalone (non-shared) CSS bundle carries the stylesheet as a file.
            var cssBundles = graph.Context.Bundles.Values.OfType<CssBundle>().ToList();
            Assert.NotEmpty(cssBundles);

            var options = new OutputOptions { IsOptimizing = false, IsReloading = false };
            var css = ReadCss(cssBundles[0], options);
            Assert.Contains(".title_", css); // hashed selector lives in the emitted file

            // The JS bundle keeps the class map but does NOT inject a <style>.
            var js = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary)
                .Stringify(options);
            Assert.Contains("title_", js);                              // class map still exported
            Assert.DoesNotContain("createElement(\"style\")", js);      // no runtime injection
            Assert.DoesNotContain(".title_", js);                       // CSS text is not inlined
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string ReadCss(CssBundle bundle, OutputOptions options)
    {
        using var stream = bundle.CreateStream(options).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task Link_mode_js_entry_appends_a_link_at_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-jslink-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "s.css"), ".box { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "import './s.css';\nexport const x = 1;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "app.js"), Array.Empty<string>(), Array.Empty<string>(), cssMode: CssMode.Link);

            var js = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary)
                .Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // No HTML document, so the chunk appends its own <link> to app.css.
            Assert.Contains("createElement(\"link\")", js);
            Assert.Contains("app.css", js);
            Assert.DoesNotContain("createElement(\"style\")", js);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Export_mode_emits_a_styles_export_and_no_css_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-export-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "s.css"), ".box { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "import './s.css';\nexport const x = 1;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "app.js"), Array.Empty<string>(), Array.Empty<string>(), cssMode: CssMode.Export);

            var js = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary)
                .Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // The chunk exports its CSS as a `styles` string; no file, no injection.
            Assert.Contains("styles", js);
            Assert.Contains(".box", js);
            Assert.DoesNotContain("createElement(\"style\")", js);
            Assert.Empty(graph.Context.Bundles.Values.OfType<CssBundle>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Split_combines_multiple_stylesheets_into_one_chunk_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-combine-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.css"), ".a { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "b.css"), ".b { color: blue; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"),
                "import './a.css';\nimport './b.css';\nexport const x = 1;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "app.js"), Array.Empty<string>(), Array.Empty<string>(), cssMode: CssMode.None);

            // Both stylesheets end up in a single combined bundle.
            var cssBundles = graph.Context.Bundles.Values.OfType<CssBundle>().ToList();
            Assert.Single(cssBundles);

            var css = ReadCss(cssBundles[0], new OutputOptions { IsOptimizing = false, IsReloading = false });
            Assert.Contains(".a", css);
            Assert.Contains(".b", css);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- HTML referencing (--css link / none for an HTML entry) -------------

    [Fact]
    public async Task Link_mode_html_references_combined_css_with_a_link_tag()
    {
        var (html, cssBundleCount) = await BuildHtmlWithCss(CssMode.Link);

        // The chunk's combined stylesheet (named after the importing chunk, app.css)
        // is referenced from <head>.
        Assert.Contains("<link", html);
        Assert.Contains("rel=\"stylesheet\"", html);
        Assert.Contains("app.css", html);
        Assert.True(cssBundleCount > 0);
    }

    [Fact]
    public async Task None_mode_html_emits_css_but_does_not_reference_it()
    {
        var (html, cssBundleCount) = await BuildHtmlWithCss(CssMode.None);

        // The combined file is still emitted as a bundle…
        Assert.True(cssBundleCount > 0);
        // …but nothing links to it.
        Assert.DoesNotContain("rel=\"stylesheet\"", html);
    }

    private static async Task<(string Html, int CssBundleCount)> BuildHtmlWithCss(CssMode mode)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-html-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "s.css"), ".box { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "import './s.css';\nexport const x = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app.js\"></script>" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(
                Path.Combine(dir, "index.html"), Array.Empty<string>(), Array.Empty<string>(), cssMode: mode);

            var options = new OutputOptions { IsOptimizing = false, IsReloading = false };
            var htmlBundle = graph.Context.Bundles.Values.First(b => b.GetFileName().EndsWith(".html"));
            using var stream = await htmlBundle.CreateStream(options);
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            var cssBundleCount = graph.Context.Bundles.Values.OfType<CssBundle>().Count();
            return (html, cssBundleCount);
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

            // These cases exercise the inline CSS-module path (class hashing +
            // runtime <style> injection), so pin the mode to `style`; the default
            // for a bare JS entry is now `none` (the build-time split).
            using var graph = await Traverse.From(
                Path.Combine(dir, entry), Array.Empty<string>(), Array.Empty<string>(), cssMode: CssMode.Style);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
