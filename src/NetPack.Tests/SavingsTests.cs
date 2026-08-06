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
                report.Recommendations!.Where(r => r.Kind == "inline-small-chunk"));

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
