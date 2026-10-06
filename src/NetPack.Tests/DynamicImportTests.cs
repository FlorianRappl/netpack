namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// Dynamic <c>import()</c> is split into a separate chunk (reported with no host
/// bundle), whereas <c>require()</c> is inlined into the current bundle. These
/// guard that distinction and that every emitted chunk is valid JavaScript.
/// </summary>
public class DynamicImportTests
{
    private static async Task<(Dictionary<JsBundle, string> Rendered, JsBundle Primary)> BundleAll(
        Action<string> setup, string entry = "main.js")
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-dyn-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            setup(dir);

            using var graph = await Traverse.From(Path.Combine(dir, entry));
            var options = new OutputOptions { IsOptimizing = false, IsReloading = false };
            var js = graph.Context.Bundles.Values.OfType<JsBundle>().ToList();
            var rendered = js.ToDictionary(b => b, b => b.Stringify(options));
            var primary = js.First(b => b.IsPrimary);
            return (rendered, primary);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertValid(string js)
        => Assert.Empty(Parser.ParseModule(js, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);

    [Fact]
    public async Task Dynamic_import_creates_a_separate_chunk()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "export function load() { return import('./lazy.js'); }");
            File.WriteAllText(Path.Combine(dir, "lazy.js"), "export const v = 'LAZY_CHUNK';");
        });

        // The lazily-imported module is a distinct bundle, not inlined.
        Assert.True(rendered.Count >= 2, $"expected a separate chunk, saw {rendered.Count} bundle(s)");

        var primaryOut = rendered[primary];
        Assert.Contains("import(", primaryOut);       // the dynamic import survives
        AssertValid(primaryOut);

        var all = string.Join("\n", rendered.Values);
        Assert.Contains("LAZY_CHUNK", all);           // and the chunk carries the code
    }

    [Fact]
    public async Task Require_is_inlined_into_the_current_bundle()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "const dep = require('./dep.js');\nexport const x = dep;");
            File.WriteAllText(Path.Combine(dir, "dep.js"), "module.exports = 'REQUIRED_INLINE';");
        });

        var primaryOut = rendered[primary];
        Assert.Contains("REQUIRED_INLINE", primaryOut); // inlined, same bundle
        AssertValid(primaryOut);
    }

    [Fact]
    public async Task Chunk_is_valid_javascript()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "export async function go() { const m = await import('./lazy.js'); return m.v; }");
            File.WriteAllText(Path.Combine(dir, "lazy.js"), "export const v = 42;");
        });

        foreach (var output in rendered.Values)
        {
            AssertValid(output);
        }
    }

    // -- constant-folded specifiers ----------------------------------------

    [Fact]
    public async Task Require_of_concatenated_string_literals_is_bundled()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "const dep = require('./de' + 'p.js');\nexport const x = dep;");
            File.WriteAllText(Path.Combine(dir, "dep.js"), "module.exports = 'FOLDED_CONCAT';");
        });

        Assert.Contains("FOLDED_CONCAT", rendered[primary]);
        AssertValid(rendered[primary]);
    }

    [Fact]
    public async Task Require_of_no_substitution_template_is_bundled()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "const dep = require(`./dep.js`);\nexport const x = dep;");
            File.WriteAllText(Path.Combine(dir, "dep.js"), "module.exports = 'FOLDED_TEMPLATE';");
        });

        Assert.Contains("FOLDED_TEMPLATE", rendered[primary]);
        AssertValid(rendered[primary]);
    }

    [Fact]
    public async Task Dynamic_import_of_concatenated_literals_is_chunked()
    {
        var (rendered, _) = await BundleAll(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "export function load() { return import('./la' + 'zy.js'); }");
            File.WriteAllText(Path.Combine(dir, "lazy.js"), "export const v = 'FOLDED_IMPORT';");
        });

        Assert.True(rendered.Count >= 2, $"expected a separate chunk, saw {rendered.Count} bundle(s)");
        Assert.Contains("FOLDED_IMPORT", string.Join("\n", rendered.Values));
    }

    [Fact]
    public async Task Genuinely_dynamic_require_is_left_untouched()
    {
        // A non-constant argument must NOT be folded — it stays a real runtime
        // require(<expr>), so Node projects with dynamic requires keep working.
        var (rendered, primary) = await BundleAll(dir =>
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "const dynName = globalThis.x;\nexport const d = require(dynName);"));

        Assert.Contains("require(dynName)", rendered[primary]);
        AssertValid(rendered[primary]);
    }

    // -- context imports ---------------------------------------------------

    [Fact]
    public async Task Context_import_expands_to_a_chunk_per_match()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "locales"));
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "export function load(lang) { return import(`./locales/${lang}.js`); }");
            File.WriteAllText(Path.Combine(dir, "locales", "en.js"), "export const msg = 'EN_LOCALE';");
            File.WriteAllText(Path.Combine(dir, "locales", "fr.js"), "export const msg = 'FR_LOCALE';");
            File.WriteAllText(Path.Combine(dir, "locales", "notes.txt"), "ignored");
        });

        // One lazy chunk per matching file (plus the primary).
        Assert.True(rendered.Count >= 3, $"expected the primary + 2 locale chunks, saw {rendered.Count}");

        var all = string.Join("\n", rendered.Values);
        Assert.Contains("EN_LOCALE", all);
        Assert.Contains("FR_LOCALE", all);

        // The primary selects the chunk by the runtime key.
        Assert.Contains("[lang]", rendered[primary]);

        foreach (var output in rendered.Values)
        {
            AssertValid(output);
        }
    }

    [Fact]
    public async Task Context_import_with_no_matches_is_left_as_a_runtime_import()
    {
        var (rendered, primary) = await BundleAll(dir =>
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "export function load(x) { return import(`./missing/${x}.js`); }"));

        // Nothing to expand → no extra chunk, and the template import stays.
        Assert.Equal(1, rendered.Count);
        Assert.Contains("./missing/", rendered[primary]);
        AssertValid(rendered[primary]);
    }

    [Fact]
    public async Task Context_import_globs_recursively()
    {
        var (rendered, _) = await BundleAll(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "locales", "sub"));
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "export function load(k) { return import(`./locales/${k}.js`); }");
            File.WriteAllText(Path.Combine(dir, "locales", "en.js"), "export const m = 'EN_TOP';");
            File.WriteAllText(Path.Combine(dir, "locales", "sub", "fr.js"), "export const m = 'FR_NESTED';");
        });

        var all = string.Join("\n", rendered.Values);
        Assert.Contains("EN_TOP", all);
        Assert.Contains("FR_NESTED", all);   // nested file matched with key "sub/fr"
        foreach (var output in rendered.Values) AssertValid(output);
    }

    [Fact]
    public async Task Context_require_inlines_matches_synchronously()
    {
        var (rendered, primary) = await BundleAll(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "cmd"));
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "export function get(name) { return require(`./cmd/${name}.js`); }");
            File.WriteAllText(Path.Combine(dir, "cmd", "a.js"), "module.exports = 'CMD_A';");
            File.WriteAllText(Path.Combine(dir, "cmd", "b.js"), "module.exports = 'CMD_B';");
        });

        // require is synchronous → the matches are inlined into the one bundle.
        Assert.Equal(1, rendered.Count);
        Assert.Contains("CMD_A", rendered[primary]);
        Assert.Contains("CMD_B", rendered[primary]);
        Assert.Contains("[name]", rendered[primary]);
        AssertValid(rendered[primary]);
    }
}
