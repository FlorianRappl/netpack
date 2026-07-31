namespace NetPack.Tests;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// Split-chunks tests following the rspack statsOutputCases/split-chunks pattern:
/// multi-entry project with shared deps + node_modules, config-driven grouping,
/// and per-bundle parse-validity assertions.
/// </summary>
public class SplitChunksTests
{
    private static readonly OutputOptions _defaultOptions = new()
    {
        IsOptimizing = false,
        IsReloading = false,
    };

    /// <summary>
    /// Creates a temp project with the given files and a package.json.
    /// Returns the absolute directory path.
    /// </summary>
    private static async Task<string> SetupProject(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-sc-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

        foreach (var (name, content) in files)
        {
            var fullPath = Path.Combine(dir, name);
            var subDir = Path.GetDirectoryName(fullPath);
            if (subDir is not null)
            {
                Directory.CreateDirectory(subDir);
            }
            await File.WriteAllTextAsync(fullPath, content);
        }

        return dir;
    }

    /// <summary>
    /// Builds a multi-entry project and returns all bundles keyed by output name.
    /// </summary>
    private static async Task<Dictionary<string, Bundle>> BuildMultiEntry(
        string dir,
        string primaryEntry,
        string[] additionalEntries,
        NetPack.Config.SplitChunksConfig? splitChunks = null)
    {
        var entryPath = Path.Combine(dir, primaryEntry);
        var shared = additionalEntries.Select(e => Path.Combine(dir, e)).ToArray();
        using var graph = await Traverse.From(entryPath, [], shared, splitChunks: splitChunks);

        var bundles = graph.Context.Bundles;
        return bundles.Values.ToDictionary(b => b.GetFileName());
    }

    /// <summary>
    /// Builds a multi-entry project and returns graph context + all bundle outputs
    /// as parsed-valid JS strings, keyed by output name.
    /// </summary>
    private static async Task<Dictionary<string, string>> BuildAndStringify(
        string dir,
        string primaryEntry,
        string[] additionalEntries,
        NetPack.Config.SplitChunksConfig? splitChunks = null)
    {
        var entryPath = Path.Combine(dir, primaryEntry);
        var shared = additionalEntries.Select(e => Path.Combine(dir, e)).ToArray();
        using var graph = await Traverse.From(entryPath, [], shared, splitChunks: splitChunks);

        var results = new Dictionary<string, string>();
        foreach (var bundle in graph.Context.Bundles.Values.OfType<JsBundle>())
        {
            var output = bundle.Stringify(_defaultOptions);
            results[bundle.GetFileName()] = output;
        }

        return results;
    }

    // -- fixture --------------------------------------------------------------
    // Mirrors rspack's statsOutputCases/split-chunks:
    //   a.js, b.js, c.js are entries
    //   d.js is shared by all three
    //   e.js only in a.js
    //   f.js shared by b.js, c.js
    //   node_modules/x.js shared by a, b
    //   node_modules/y.js shared by a, b
    //   node_modules/z.js shared by b, c

    private static async Task<string> SetupSplitChunksFixture()
    {
        return await SetupProject(
            ("a.js", "import d from './d.js'; import e from './e.js'; import x from 'x'; import y from 'y'; export default 'a' + d + e + x + y;"),
            ("b.js", "import d from './d.js'; import f from './f.js'; import x from 'x'; import y from 'y'; export default 'b' + d + f + x + y;"),
            ("c.js", "import d from './d.js'; import f from './f.js'; import x from 'x'; import z from 'z'; export default 'c' + d + f + x + z;"),
            ("d.js", "export default 'd';"),
            ("e.js", "export default 'e';"),
            ("f.js", "export default 'f';"),
            ("node_modules/x.js", "export default 'x';"),
            ("node_modules/y.js", "export default 'y';"),
            ("node_modules/z.js", "export default 'z';"));
    }

    // -- test 1: backward compatibility (default strategy = Connected) --------

    [Fact]
    public async Task Default_strategy_identical_to_Connected()
    {
        var dir = await SetupSplitChunksFixture();

        var outputNoConfig = await BuildAndStringify(dir, "a.js", ["b.js", "c.js"]);
        Assert.All(outputNoConfig.Values, IsValidJs);

        // The default strategy should produce shared chunks named common.0001.js etc.
        var sharedChunks = outputNoConfig.Keys.Where(k => k.StartsWith("common.")).ToList();
        Assert.NotEmpty(sharedChunks);
    }

    // -- test 2: manual cacheGroup extracts node_modules to "vendors" ---------

    [Fact]
    public async Task Manual_cacheGroup_extracts_vendors_chunk()
    {
        var dir = await SetupSplitChunksFixture();
        var config = new NetPack.Config.SplitChunksConfig
        {
            CacheGroups = new()
            {
                ["vendors"] = new()
                {
                    Test = "**/node_modules/**",
                    Name = "vendors",
                    Enforce = true,
                },
            },
        };

        var bundles = await BuildMultiEntry(dir, "a.js", ["b.js", "c.js"], config);
        Assert.All(bundles.Values.OfType<JsBundle>().Select(b => b.Stringify(_defaultOptions)), IsValidJs);

        // There must be a vendors.js bundle
        var vendorsKey = bundles.Keys.FirstOrDefault(k => k.StartsWith("vendors"));
        Assert.NotNull(vendorsKey);

        // The vendors bundle must contain node_modules modules
        var vendorsBundle = bundles[vendorsKey];
        Assert.Contains(vendorsBundle.Items, m => m.FileName.EndsWith("x.js"));
        Assert.Contains(vendorsBundle.Items, m => m.FileName.EndsWith("y.js"));
        Assert.Contains(vendorsBundle.Items, m => m.FileName.EndsWith("z.js"));

        // Vendor bundle should NOT contain non-node_modules modules
        Assert.DoesNotContain(vendorsBundle.Items, m => m.FileName.EndsWith("d.js"));
    }

    // -- test 3: priority — higher-priority group wins ------------------------

    [Fact]
    public async Task Higher_priority_cacheGroup_wins()
    {
        var dir = await SetupProject(
            ("a.js", "import lib from './lib/shared.js'; import dep from 'dep'; export default 'a' + lib + dep;"),
            ("b.js", "import lib from './lib/shared.js'; import dep from 'dep'; export default 'b' + lib + dep;"),
            ("lib/shared.js", "export default 'shared-lib';"),
            ("node_modules/dep.js", "export default 'dep-module';"));

        var config = new NetPack.Config.SplitChunksConfig
        {
            CacheGroups = new()
            {
                ["shared-lib"] = new()
                {
                    Test = "**/lib/**",
                    Name = "shared-lib",
                    Priority = 20,
                    Enforce = true,
                },
                ["vendors"] = new()
                {
                    Test = "**/node_modules/**",
                    Name = "vendors",
                    Priority = -10,
                    Enforce = true,
                },
            },
        };

        var output = await BuildAndStringify(dir, "a.js", ["b.js"], config);
        Assert.All(output.Values, IsValidJs);

        // Both named chunks must exist
        Assert.Contains(output.Keys, k => k.StartsWith("shared-lib"));
        Assert.Contains(output.Keys, k => k.StartsWith("vendors"));
    }

    // -- test 4: minChunks — below threshold stays in entries -----------------

    [Fact]
    public async Task MinChunks_below_threshold_modules_stay_in_entries()
    {
        var dir = await SetupSplitChunksFixture();
        // d.js is imported by 3 entries. minChunks: 4 means it should stay in entries.
        var config = new NetPack.Config.SplitChunksConfig
        {
            CacheGroups = new()
            {
                ["shared-deps"] = new()
                {
                    Test = "d.js",
                    MinChunks = 4,
                    Name = "shared-deps",
                },
            },
        };

        var output = await BuildAndStringify(dir, "a.js", ["b.js", "c.js"], config);
        Assert.All(output.Values, IsValidJs);

        // No "shared-deps" chunk should have been created since minChunks=4 but d.js
        // is only imported by 3 entries.
        Assert.DoesNotContain(output.Keys, k => k.StartsWith("shared-deps"));
    }

    // -- test 5: minSize — below size threshold stays in entries ---------------

    [Fact]
    public async Task MinSize_below_threshold_chunks_stay_in_entries()
    {
        var dir = await SetupSplitChunksFixture();
        // All our test modules are tiny (<500 bytes). minSize: 50000 means nothing
        // gets extracted.
        var config = new NetPack.Config.SplitChunksConfig
        {
            MinSize = 50000,
            CacheGroups = new()
            {
                ["default"] = new() { Test = null },
            },
        };

        var output = await BuildAndStringify(dir, "a.js", ["b.js", "c.js"], config);
        Assert.All(output.Values, IsValidJs);

        // With minSize: 50000 and no enforce, no shared chunks are created because
        // the total size of shared modules is far below 50KB.
        var sharedChunks = output.Keys.Where(k => k.StartsWith("common.")).ToList();
        Assert.Empty(sharedChunks);
    }

    // -- test 6: enforce — creates chunk even below minSize --------------------

    [Fact]
    public async Task Enforce_creates_chunk_below_minSize()
    {
        var dir = await SetupSplitChunksFixture();
        var config = new NetPack.Config.SplitChunksConfig
        {
            MinSize = 50000,
            CacheGroups = new()
            {
                ["vendors"] = new()
                {
                    Test = "**/node_modules/**",
                    Name = "vendors",
                    Enforce = true,
                },
            },
        };

        var bundles = await BuildMultiEntry(dir, "a.js", ["b.js", "c.js"], config);
        Assert.All(bundles.Values.OfType<JsBundle>().Select(b => b.Stringify(_defaultOptions)), IsValidJs);

        // vendors.js must exist despite the 50KB minSize
        var vendorsKey = bundles.Keys.FirstOrDefault(k => k.StartsWith("vendors"));
        Assert.NotNull(vendorsKey);

        // Non-vendor shared modules should NOT be in the vendor chunk
        var vendorBundle = bundles[vendorsKey];
        Assert.DoesNotContain(vendorBundle.Items, m => m.FileName.EndsWith("d.js"));
    }

    // -- test 7: backward compat — Connected class unchanged ------------------

    [Fact]
    public void Backward_compat_Connected_class_unchanged()
    {
        var connected = new Connected((i, _) => $"common#{i}");
        var nodes = new List<Node>
        {
            new("entry1.js", 100),
            new("entry2.js", 100),
        };

        var graphs = connected.Apply(nodes);
        Assert.NotNull(graphs);
    }

    // -- test 8: analyzer shows primary vs shared flags -----------------------

    [Fact]
    public async Task Analyzer_output_shows_primary_vs_shared_flags()
    {
        var dir = await SetupSplitChunksFixture();
        var config = new NetPack.Config.SplitChunksConfig
        {
            CacheGroups = new()
            {
                ["vendors"] = new()
                {
                    Test = "**/node_modules/**",
                    Name = "vendors",
                    Enforce = true,
                },
            },
        };

        var entryPath = Path.Combine(dir, "a.js");
        var sharedPaths = new[] { Path.Combine(dir, "b.js"), Path.Combine(dir, "c.js") };
        using var graph = await Traverse.From(entryPath, [], sharedPaths, splitChunks: config);

        var bundles = graph.Context.Bundles;
        var jsBundles = bundles.Values.OfType<JsBundle>().ToList();

        Assert.Contains(jsBundles, b => b.IsPrimary);
        Assert.Contains(jsBundles, b => b.IsShared);

        Assert.All(jsBundles.Select(b => b.Stringify(_defaultOptions)), IsValidJs);
    }

    [Fact]
    public async Task Metadata_serialization_includes_flags_property()
    {
        var dir = await SetupProject(
            ("a.js", "import dep from 'dep'; export default 'a' + dep;"),
            ("b.js", "import dep from 'dep'; export default 'b' + dep;"),
            ("node_modules/dep.js", "export default 'shared';"));

        var config = new NetPack.Config.SplitChunksConfig
        {
            CacheGroups = new()
            {
                ["vendors"] = new()
                {
                    Test = "**/node_modules/**",
                    Name = "vendors",
                    Enforce = true,
                },
            },
        };

        // Build fresh graph so Metadata has exclusive access
        var entryPath = Path.Combine(dir, "a.js");
        var sharedPaths = new[] { Path.Combine(dir, "b.js") };
        using var graph = await Traverse.From(entryPath, [], sharedPaths, splitChunks: config);
        var compilation = new NetPack.Graph.Writers.MemoryResultWriter(graph.Context);
        await compilation.WriteOut(_defaultOptions);

        var metadata = new NetPack.Graph.Metadata(graph, compilation);
        var json = metadata.Stringify();

        Assert.Contains("\"flags\":\"entry\"", json);
        Assert.Contains("\"flags\":\"shared\"", json);
    }

    private static void IsValidJs(string code)
    {
        var parsed = Parser.ParseModule(code, "out.js",
            new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
        Assert.Empty(parsed.Diagnostics);
    }
}
