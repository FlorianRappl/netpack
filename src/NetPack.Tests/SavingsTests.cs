namespace NetPack.Tests;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Savings;
using Xunit;

/// <summary>
/// Tests for the bundle-shape savings analysis surfaced by the <c>analyze</c>
/// command: it inspects the finished chunk graph for duplicated modules and
/// poorly shaped shared chunks and turns them into actionable recommendations.
/// </summary>
public class SavingsTests
{
    private static async Task<string> SetupProject(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-savings-" + Path.GetRandomFileName());
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

    [Fact]
    public async Task Small_chunk_shared_by_two_entries_is_flagged_for_inlining()
    {
        var dir = await SetupProject(
            ("a.js", "import s from './shared.js'; export default 'a' + s;"),
            ("b.js", "import s from './shared.js'; export default 'b' + s;"),
            ("shared.js", "export default 'shared';"));

        try
        {
            using var graph = await Traverse.From(
                Path.Combine(dir, "a.js"), [], [Path.Combine(dir, "b.js")]);

            var report = SavingsAnalyzer.Analyze(graph.Context);

            Assert.NotNull(report.Recommendations);
            var rec = Assert.Single(
                report.Recommendations!, r => r.Kind == "inline-small-chunk");

            // A small shared chunk pulled in by exactly two entries: inlining it
            // removes one request in exchange for a little duplicated code.
            Assert.Equal("medium", rec.Severity);
            Assert.Equal(1, rec.Requests);
            Assert.True(rec.Bytes <= 0, "inlining a chunk should not save bytes");
            Assert.NotNull(rec.Bundles);
            Assert.Equal(3, rec.Bundles!.Count); // the chunk + both entries
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Well_shaped_single_entry_has_no_recommendations()
    {
        var dir = await SetupProject(
            ("index.js", "import a from './a.js'; import b from './b.js'; export default a + b;"),
            ("a.js", "export default 'a';"),
            ("b.js", "export default 'b';"));

        try
        {
            using var graph = await Traverse.From(Path.Combine(dir, "index.js"));

            var report = SavingsAnalyzer.Analyze(graph.Context);

            Assert.Equal(0, report.PotentialBytes);
            Assert.Null(report.Recommendations);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Oversized_bundle_and_dominant_module_are_flagged()
    {
        // A single module larger than the 244 KB budget: it makes the whole
        // bundle oversized and dominates it.
        var big = "export default \"" + new string('x', 300 * 1024) + "\";";

        var dir = await SetupProject(
            ("index.js", "import big from './big.js'; export default big;"),
            ("big.js", big));

        try
        {
            using var graph = await Traverse.From(Path.Combine(dir, "index.js"));

            var report = SavingsAnalyzer.Analyze(graph.Context);

            Assert.NotNull(report.Recommendations);
            var oversized = Assert.Single(report.Recommendations!, r => r.Kind == "oversized-bundle");
            // The advice should name a concrete lazy-load candidate, not just "split it".
            Assert.NotNull(oversized.Modules);
            Assert.Contains(oversized.Modules!, m => m.EndsWith("big.js"));
            Assert.Contains(report.Recommendations!, r => r.Kind == "heavy-module");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Small_single_use_asset_is_flagged_for_inlining()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-savings-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
            "import logo from './logo.png'; export default logo;");
        await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[64]);

        try
        {
            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));

            // Default inline limit (0): the asset ships as a separate file.
            var report = SavingsAnalyzer.Analyze(graph.Context);

            Assert.NotNull(report.Recommendations);
            var rec = Assert.Single(report.Recommendations!, r => r.Kind == "inline-asset");
            Assert.Equal(1, rec.Requests);
            Assert.True(rec.Bytes < 0, "inlining adds bytes to the bundle");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Large_inlined_asset_is_flagged_to_stop_inlining()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-savings-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
            "import hero from './hero.png'; export default hero;");
        await File.WriteAllBytesAsync(Path.Combine(dir, "hero.png"), new byte[10 * 1024]);

        try
        {
            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));

            // With a generous inline limit the 10 KB asset is inlined — and, being
            // large and single-use, it is better off as a cacheable file.
            var report = SavingsAnalyzer.Analyze(graph.Context, inlineLimit: 32 * 1024);

            Assert.NotNull(report.Recommendations);
            var rec = Assert.Single(report.Recommendations!, r => r.Kind == "stop-inlining-asset");
            Assert.True(rec.Bytes > 0, "emitting a file removes bytes from the bundle");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Partially_used_side_effect_module_is_flagged_as_a_trap()
    {
        var payload = "export const payload = \"" + new string('x', 12 * 1024) + "\";";

        var dir = await SetupProject(
            ("main.js", "import { used } from './side.js'; export default used();"),
            // A top-level statement makes the module non-pure, so importing just
            // `used` still drags in the large unused `payload` export.
            ("side.js", "console.log('init');\nexport function used() { return 1; }\n" + payload));

        try
        {
            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));

            var report = SavingsAnalyzer.Analyze(graph.Context);

            Assert.NotNull(report.Recommendations);
            var rec = Assert.Single(report.Recommendations!, r => r.Kind == "side-effect-trap");
            Assert.NotNull(rec.Modules);
            Assert.Contains(rec.Modules!, m => m.EndsWith("side.js"));
            Assert.True(rec.Bytes > 0, "the trap should estimate the unused weight carried in");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Savings_report_is_embedded_in_the_metafile()
    {
        var dir = await SetupProject(
            ("a.js", "import s from './shared.js'; export default 'a' + s;"),
            ("b.js", "import s from './shared.js'; export default 'b' + s;"),
            ("shared.js", "export default 'shared';"));

        try
        {
            using var graph = await Traverse.From(
                Path.Combine(dir, "a.js"), [], [Path.Combine(dir, "b.js")]);

            var json = Traverse.BuildMetafile(graph.Context, []);

            Assert.Contains("\"savings\"", json);
            Assert.Contains("\"recommendations\"", json);
            Assert.Contains("inline-small-chunk", json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
