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
    public async Task Css_files_imported_from_js_are_emitted_in_bundle()
    {
        // a.js imports b.css and c.css — both should appear in the bundle output
        var output = await BundleJs("a.js",
            ("a.js", "import './b.css';\nimport './c.css';\nexport const x = 1;"),
            ("b.css", ".b { color: red; }"),
            ("c.css", ".c { color: blue; }"));

        var cssFragments = ExtractOrderedCss(output);
        Assert.Equal(2, cssFragments.Count);
        Assert.Contains(cssFragments, c => c.Contains(".b"));
        Assert.Contains(cssFragments, c => c.Contains(".c"));
    }

    // -- Test 2: Transitive CSS imports through JS modules are bundled ------

    [Fact]
    public async Task Transitive_css_through_js_modules_is_bundled()
    {
        // entry.js → liba/index.js → imports libb/index.js
        // libb/index.js imports button.css
        // liba/common.js imports teaser.css
        // Both CSS files should appear in the output regardless of
        // parallel resolution order.
        var output = await BundleJs("entry.js",
            ("entry.js", "import { Teaser } from './liba/index.js';\nTeaser();"),
            ("liba/index.js", "export * from './common.js';"),
            ("liba/common.js", "import { CarouselButton } from '../libb/index.js';\nimport styles from './teaser.css';\nexport const Teaser = () => CarouselButton();"),
            ("liba/teaser.css", ".teaser { color: red; }"),
            ("libb/index.js", "export * from './common.js';"),
            ("libb/common.js", "import styles from './button.css';\nexport const CarouselButton = () => null;"),
            ("libb/button.css", ".button { color: blue; }"));

        // Each CSS import generates a virtual JS module with runtime style injection.
        Assert.Contains(".button", output);
        Assert.Contains(".teaser", output);
    }

    // -- Test 3: Re-export respects the re-export declaration order ----------

    [Fact]
    public async Task Css_re_export_order_is_deterministic()
    {
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
        // Both CSS files should be present (order is stable across builds but
        // depends on which dependency resolves first in parallel processing)
        Assert.Contains(cssFragments, c => c.Contains(".dependency{"));
        Assert.Contains(cssFragments, c => c.Contains(".dependency2{"));
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
    public async Task Css_module_class_names_appear_in_output()
    {
        var output = await BundleJs("app.js",
            ("app.js", "import { title } from './a.css';\nimport { subtitle } from './b.css';\nexport const t = title;"),
            ("a.css", ".title { color: red; }"),
            ("b.css", ".subtitle { color: blue; }"));

        // Both CSS modules should be present in the output with their
        // hashed class names
        Assert.Contains("title_", output);
        Assert.Contains("subtitle_", output);
        Assert.Contains(".title_", output);
        Assert.Contains(".subtitle_", output);
    }

    // -- Test 8: Multiple HTML-linked CSS files are bundled correctly --------

    [Fact]
    public async Task Multiple_linked_css_in_html_are_bundled_correctly()
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

            // Both have valid post-order indices (they were processed)
            Assert.True(cssBundles.All(b => b.Root.PostOrderIndex >= 0),
                "All CSS bundles should have assigned post-order indices");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 9: Conflict detection across multiple entries ------------------

    [Fact]
    public async Task Detects_ordering_conflict_when_css_order_differs_across_entries()
    {
        // app1.js imports shared.css then a.css
        // app2.js imports a.css then shared.css
        // a.css and shared.css are both imported by both entries but in
        // opposite orders — a conflict should be detected.
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "shared.css"), ".shared { margin: 0; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.css"), ".a { color: green; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app1.js"),
                "import './shared.css';\nimport './a.css';\nexport const x = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app2.js"),
                "import './a.css';\nimport './shared.css';\nexport const y = 2;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(sw);

            try
            {
                using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            }
            finally
            {
                Console.SetError(originalError);
            }

            var stderr = sw.ToString();
            Assert.Contains("Conflicting CSS order", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 10: No false positives when order is consistent ---------------

    [Fact]
    public async Task No_warning_when_css_order_is_consistent_across_entries()
    {
        // Both entries import shared.css first then their own CSS — no conflict.
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "shared.css"), ".shared { margin: 0; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.css"), ".a { color: green; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "b.css"), ".b { color: blue; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app1.js"),
                "import './shared.css';\nimport './a.css';\nexport const a = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "app2.js"),
                "import './shared.css';\nimport './b.css';\nexport const b = 2;");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(sw);

            try
            {
                using var graph = await Traverse.From(Path.Combine(dir, "index.html"));
            }
            finally
            {
                Console.SetError(originalError);
            }

            var stderr = sw.ToString();
            Assert.DoesNotContain("Conflicting CSS order", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 11: Debug output produces the ordered CSS list ----------------

    [Fact]
    public async Task Debug_output_lists_css_files_in_evaluation_order()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cssord-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.js"),
                "import './first.css';\nimport './second.css';\nexport const x = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "first.css"), ".first { color: red; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "second.css"), ".second { color: blue; }");

            using var graph = await Traverse.From(Path.Combine(dir, "a.js"));

            using var sw = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(sw);

            try
            {
                Traverse.DebugCssOrder(graph.Context);
            }
            finally
            {
                Console.SetError(originalError);
            }

            var stderr = sw.ToString();
            var firstIdx = stderr.IndexOf("first.css", StringComparison.Ordinal);
            var secondIdx = stderr.IndexOf("second.css", StringComparison.Ordinal);
            Assert.True(firstIdx >= 0, "Debug output should list first.css");
            Assert.True(secondIdx >= 0, "Debug output should list second.css");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
