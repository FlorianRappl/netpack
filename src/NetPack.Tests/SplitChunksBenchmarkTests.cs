namespace NetPack.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Benchmark tests verifying chunk grouping on real projects in <c>data/</c>.
/// </summary>
public class SplitChunksBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public SplitChunksBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "CI: requires data/projects/large — run locally")]
    public async Task Large_project_bundles_with_split_chunks_and_produces_valid_output()
    {
        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "projects", "large"));

        if (!Directory.Exists(projectDir))
        {
            _output.WriteLine($"Project dir not found: {projectDir}");
            return;
        }

        var entryPath = Path.Combine(projectDir, "src", "index.html");
        if (!File.Exists(entryPath))
        {
            _output.WriteLine($"Entry not found: {entryPath}");
            return;
        }

        var config = new NetPack.Config.SplitChunksConfig
        {
            MinSize = 0,
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

        var sw = Stopwatch.StartNew();
        using var graph = await Traverse.From(entryPath, [], [], splitChunks: config);
        sw.Stop();

        var bundles = graph.Context.Bundles;
        var jsBundles = bundles.Values.OfType<JsBundle>().ToList();
        var primaryCount = jsBundles.Count(b => b.IsPrimary);
        var sharedCount = jsBundles.Count(b => b.IsShared);

        _output.WriteLine($"Graph build: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Total bundles: {bundles.Count}, JS bundles: {jsBundles.Count}");
        _output.WriteLine($"Primary: {primaryCount}, Shared: {sharedCount}");

        var options = new OutputOptions { IsOptimizing = true, IsReloading = false };
        foreach (var bundle in jsBundles)
        {
            var output = bundle.Stringify(options);
            var parsed = Parser.ParseModule(output, bundle.GetFileName(),
                new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
            Assert.Empty(parsed.Diagnostics);
        }

        _output.WriteLine("All bundles parse-valid.");
    }
}
