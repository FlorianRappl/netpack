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
/// A synthetic performance probe for the hot path that matters most for large
/// applications: many mixed JS/TS modules feeding one minified bundle.
///
/// The assertions keep it deterministic and correctness-focused, while the
/// logged timings make it useful when profiling graph construction versus
/// minified emission.
/// </summary>
public class PerformanceStressTests
{
    private const int ModuleCount = 96;

    private readonly ITestOutputHelper _output;

    public PerformanceStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Mixed_js_ts_module_graph_minifies_validly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-perf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await CreateProject(dir);

            var graphStopwatch = Stopwatch.StartNew();
            using var graph = await Traverse.From(Path.Combine(dir, "src", "app.ts"));
            graphStopwatch.Stop();

            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

            var prettyStopwatch = Stopwatch.StartNew();
            var pretty = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            prettyStopwatch.Stop();

            var minifiedStopwatch = Stopwatch.StartNew();
            var minified = bundle.Stringify(new OutputOptions { IsOptimizing = true, IsReloading = false });
            minifiedStopwatch.Stop();

            _output.WriteLine($"modules={ModuleCount} bundles={graph.Context.Bundles.Values.OfType<JsBundle>().Count()}");
            _output.WriteLine($"graph={graphStopwatch.ElapsedMilliseconds}ms pretty={prettyStopwatch.ElapsedMilliseconds}ms minified={minifiedStopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"sizes pretty={pretty.Length} minified={minified.Length} saved={pretty.Length - minified.Length}");

            Assert.True(minified.Length < pretty.Length,
                $"expected minified output to be smaller than pretty output, but pretty={pretty.Length} and minified={minified.Length}");

            var reparsed = Parser.ParseModule(minified, "out.js",
                new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
            Assert.Empty(reparsed.Diagnostics);

            Assert.Contains("ENTRY_MARKER", pretty);
            Assert.Contains("ENTRY_MARKER", minified);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task CreateProject(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);

        for (var index = 0; index < ModuleCount; index++)
        {
            var fileName = $"mod-{index:D3}.{(index % 2 == 0 ? "ts" : "js")}";
            var filePath = Path.Combine(srcDir, fileName);

            if (index == 0)
            {
                var firstModule = index % 2 == 0
                    ? "export const value000: number = 0;\n"
                    : "export const value000 = 0;\n";

                await File.WriteAllTextAsync(filePath, firstModule);
                continue;
            }

            var previous = $"./mod-{index - 1:D3}";
            var content = index % 2 == 0
                ? $"import {{ value{index - 1:D3} }} from '{previous}';\nexport const value{index:D3}: number = value{index - 1:D3} + {index};\n"
                : $"import {{ value{index - 1:D3} }} from '{previous}';\nexport const value{index:D3} = value{index - 1:D3} + {index};\n";

            await File.WriteAllTextAsync(filePath, content);
        }

        var imports = string.Join(Environment.NewLine,
            Enumerable.Range(0, ModuleCount).Select(index => $"import {{ value{index:D3} }} from './mod-{index:D3}';"));
        var total = string.Join(" + ", Enumerable.Range(0, ModuleCount).Select(index => $"value{index:D3}"));

        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.ts"),
            $"{imports}\n\nexport const total = {total};\nexport const marker = 'ENTRY_MARKER';\n");
    }
}