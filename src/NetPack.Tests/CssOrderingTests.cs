namespace NetPack.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

/// <summary>
/// Deterministic CSS ordering based on JS module evaluation order.
/// CSS files imported from JS are emitted in the same relative order as their
/// importing modules appear in the evaluation chain, so the cascade matches
/// runtime execution order.
/// </summary>
public class CssOrderingTests
{
    private static readonly OutputOptions _defaultOptions = new()
    {
        IsOptimizing = false,
        IsReloading = false,
    };

    private static async Task<string> BundleJs(string entry, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            if (!files.Any(f => f.Name == "package.json"))
            {
                await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            }

            foreach (var (name, content) in files)
            {
                var fullPath = Path.Combine(dir, name);
                var subDir = Path.GetDirectoryName(fullPath);
                if (subDir is not null && !Directory.Exists(subDir))
                    Directory.CreateDirectory(subDir);
                await File.WriteAllTextAsync(fullPath, content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, entry));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(_defaultOptions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Extracts the CSS content from a JS bundle output in the order the runtime
    /// injects it (which matches the module ID order in the registry).
    /// </summary>
    private static List<string> ExtractOrderedCss(string bundleOutput)
    {
        var result = new List<string>();
        var searchFrom = 0;
        while (true)
        {
            var start = bundleOutput.IndexOf("const __css = \"", searchFrom, System.StringComparison.Ordinal);
            if (start < 0) break;

            start += "const __css = \"".Length;
            var end = bundleOutput.IndexOf("\";", start, System.StringComparison.Ordinal);
            if (end < 0) break;

            result.Add(bundleOutput[start..end]);
            searchFrom = end + 2;
        }

        return result;
    }

    // -- Test 1: Basic CSS ordering respects JS import order ----------------

    [Fact]
    public async Task Css_order_respects_js_import_declaration_order()
    {
        // a.js imports b.css first, then c.css — b.css should appear before c.css
        var output = await BundleJs("a.js",
            ("a.js", "import './b.css';\nimport './c.css';\nexport const x = 1;"),
            ("b.css", ".b { color: red; }"),
            ("c.css", ".c { color: blue; }"));

        var cssFragments = ExtractOrderedCss(output);
        Assert.Equal(2, cssFragments.Count);
        Assert.Contains(".b", cssFragments[0]);
        Assert.Contains(".c", cssFragments[1]);
    }

    // -- Test 2: Transitive CSS ordering through JS modules -----------------

    [Fact]
    public async Task Transitive_css_order_follows_module_evaluation_order()
    {
        // entry.js → liba/index.js (exports Teaser) → imports libb/index.js (exports CarouselButton)
        // libb/index.js imports button.css
        // liba/common.js imports teaser.css
        // Since libb is evaluated before liba (liba imports libb),
        // button.css should come before teaser.css
        var output = await BundleJs("entry.js",
            ("entry.js", "import { Teaser } from './liba/index.js';\nTeaser();"),
            ("liba/index.js", "export * from './common.js';"),
            ("liba/common.js", "import { CarouselButton } from '../libb/index.js';\nimport styles from './teaser.css';\nexport const Teaser = () => CarouselButton();"),
            ("liba/teaser.css", ".teaser { color: red; }"),
            ("libb/index.js", "export * from './common.js';"),
            ("libb/common.js", "import styles from './button.css';\nexport const CarouselButton = () => null;"),
            ("libb/button.css", ".button { color: blue; }"));

        // Look for the CSS in the output - each CSS import generates a virtual JS
        // module with runtime style injection. The styles should appear in the
        // order their JS imports resolve: libb (button) before liba (teaser).
        var cssFragments = ExtractOrderedCss(output);
        Assert.True(cssFragments.Count >= 2);
        var buttonIdx = cssFragments.FindIndex(c => c.Contains(".button"));
        var teaserIdx = cssFragments.FindIndex(c => c.Contains(".teaser"));
        Assert.True(buttonIdx >= 0, "button.css should be present");
        Assert.True(teaserIdx >= 0, "teaser.css should be present");
        Assert.True(buttonIdx < teaserIdx, "button.css should appear before teaser.css");
    }

    // -- Test 3: Re-export ordering -----------------------------------------

    [Fact]
    public async Task Css_re_export_order_matches_re_export_declaration_order()
    {
        // entry.js imports component, which re-exports from dependency/index.js
        // dependency/index.js does `export * from './dependency2.js'` first,
        // then `export * from './dependency.js'` — dependency2.js is evaluated
        // first, so its CSS should appear before dependency.css in the output.
        var output = await BundleJs("entry.js",
            ("entry.js", "const { component } = require('./component.js');\ncomponent();"),
            ("component.js",
                "export { dependency, dependency2 } from './dependency/index.js';\n" +
                "export function component() {}"),
            ("dependency/index.js", "export * from './dependency2.js';\nexport * from './dependency.js';"),
            ("dependency/dependency.js", "import './dependency.css';\nexport function dependency() {}"),
            ("dependency/dependency.css", ".dependency { color: red; }"),
            ("dependency/dependency2.js", "import './dependency2.css';\nexport function dependency2() {}"),
            ("dependency/dependency2.css", ".dependency2 { color: blue; }"));

        var cssFragments = ExtractOrderedCss(output);
        // Use distinct patterns: "dependency{" uniquely identifies .dependency without matching .dependency2
        var depIdx = cssFragments.FindIndex(c => c.Contains(".dependency{"));
        var dep2Idx = cssFragments.FindIndex(c => c.Contains(".dependency2{"));
        Assert.True(depIdx >= 0, "dependency.css should be present");
        Assert.True(dep2Idx >= 0, "dependency2.css should be present");
        Assert.True(dep2Idx < depIdx, "dependency2.css (re-exported first) should appear before dependency.css");
    }

    // -- Test 4: Stable ordering across repeated builds ---------------------

    [Fact]
    public async Task Css_order_is_stable_across_repeated_builds()
    {
        var files = new[]
        {
            ("a.js", "import './b.css';\nimport './c.css';\nimport './d.css';\nexport const x = 1;"),
            ("b.css", ".b { color: red; }"),
            ("c.css", ".c { color: green; }"),
            ("d.css", ".d { color: blue; }"),
        };

        var first = await BundleJs("a.js", files);
        var second = await BundleJs("a.js", files);

        Assert.Equal(first, second);
    }

    // -- Test 5: CSS from HTML <link> vs JS import order --------------------

    [Fact]
    public async Task Html_linked_css_comes_before_js_imported_css()
    {
        // index.html has <link rel="stylesheet" href="global.css">
        // entry.js imports component.css
        // global.css should appear first in the HTML output (linked before scripts)
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "global.css"), ".global { margin: 0; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "component.css"), ".component { padding: 20px; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "import './component.css';\nexport const app = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<link rel=\"stylesheet\" href=\"./global.css\">" +
                "<script type=\"module\" src=\"./app.js\"></script>" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            var htmlBundle = graph.Context.Bundles.Values.OfType<HtmlBundle>().First(b => b.IsPrimary);
            using var stream = await htmlBundle.CreateStream(new OutputOptions { IsOptimizing = false, IsReloading = false });
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            var globalLinkIdx = html.IndexOf("global.", System.StringComparison.Ordinal);
            var scriptIdx = html.IndexOf("<script", System.StringComparison.Ordinal);
            Assert.True(globalLinkIdx >= 0);
            Assert.True(scriptIdx >= 0);
            Assert.True(globalLinkIdx < scriptIdx, "global.css link should appear before scripts in HTML");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 6: Multiple entries with shared CSS emit without error ---------

    [Fact]
    public async Task Multiple_entries_with_shared_css_produce_valid_output()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "shared.css"), ".common { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.css"), ".a { color: green; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "b.css"), ".b { color: blue; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app1.js"),
                "import './shared.css';\nimport './a.css';\nexport const a = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app2.js"),
                "import './b.css';\nimport './shared.css';\nexport const b = 2;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));

            // Verify shared CSS chunk was created
            var sharedCssBundles = graph.Context.Bundles.Values
                .OfType<CssBundle>()
                .Where(b => b.IsShared)
                .ToList();
            Assert.NotEmpty(sharedCssBundles);

            // Verify non-shared CSS is inlined in the JS bundles
            var jsBundles = graph.Context.Bundles.Values.OfType<JsBundle>().ToList();
            Assert.True(jsBundles.Count >= 2);

            // Each JS bundle should have its non-shared CSS inlined
            var combinedOutput = string.Join("", jsBundles.Select(b => b.Stringify(_defaultOptions)));

            // At least one should contain the non-shared CSS
            Assert.Contains(".a", combinedOutput);
            Assert.Contains(".b", combinedOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 7: CSS module ordering is deterministic -----------------------

    [Fact]
    public async Task Css_module_class_names_are_ordered_deterministically()
    {
        // Importing multiple CSS modules in a specific order should produce
        // consistent class-hash export order.
        var output = await BundleJs("app.js",
            ("app.js", "import { title } from './a.css';\nimport { subtitle } from './b.css';\nexport const t = title;"),
            ("a.css", ".title { color: red; }"),
            ("b.css", ".subtitle { color: blue; }"));

        var cssFragments = ExtractOrderedCss(output);
        Assert.True(cssFragments.Count >= 2);
        var titleIdx = cssFragments.FindIndex(c => c.Contains(".title"));
        var subtitleIdx = cssFragments.FindIndex(c => c.Contains(".subtitle"));
        Assert.True(titleIdx >= 0);
        Assert.True(subtitleIdx >= 0);
        Assert.True(titleIdx < subtitleIdx, "a.css should appear before b.css");
    }

    // -- Test 8: Build-time linked CSS ordering in HTML output --------------

    [Fact]
    public async Task Multiple_linked_css_in_html_preserves_source_order()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "first.css"), ".first { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "second.css"), ".second { color: blue; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<link rel=\"stylesheet\" href=\"./first.css\">" +
                "<link rel=\"stylesheet\" href=\"./second.css\">" +
                "</head><body></body></html>");

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));

            // Both are CSS bundles referenced by the HTML
            var cssBundles = graph.Context.Bundles.Values.OfType<CssBundle>().ToList();
            Assert.Equal(2, cssBundles.Count);

            // Post-order indices should match source order (first.css processed before second.css)
            var firstBundle = cssBundles.First(b => b.Name.Contains("first"));
            var secondBundle = cssBundles.First(b => b.Name.Contains("second"));
            Assert.True(firstBundle.Root.PostOrderIndex < secondBundle.Root.PostOrderIndex,
                "first.css should have a lower post-order index than second.css");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
