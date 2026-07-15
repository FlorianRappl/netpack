namespace NetPack.Tests;

using System.IO;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Writers;
using Xunit;

/// <summary>
/// Guards against stale-tail corruption: a rebuild that produces a shorter file
/// than the one already on disk must fully replace it. A non-truncating open
/// (<c>File.OpenWrite</c> / <c>FileMode.OpenOrCreate</c>) would leave trailing
/// bytes from the previous, longer build in place — which manifested as a bundle
/// containing a second, spliced-in copy of the runtime + <c>export default</c>
/// trailer and a <c>SyntaxError</c> at runtime.
/// </summary>
public class DiskWriterTests
{
    [Fact]
    public async Task Rebuild_over_a_longer_file_leaves_no_stale_tail()
    {
        var src = Path.Combine(Path.GetTempPath(), "netpack-src-" + Path.GetRandomFileName());
        var outdir = Path.Combine(Path.GetTempPath(), "netpack-out-" + Path.GetRandomFileName());
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(outdir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(src, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(src, "app.js"), "export const value = 1 + 2;");

            using var graph = await Traverse.From(Path.Combine(src, "app.js"));
            var options = new OutputOptions { IsOptimizing = true, IsReloading = false };

            // Simulate a previous, much longer build by pre-filling every target file
            // with filler. A non-truncating writer would leave this tail behind.
            const string sentinel = "STALE_TAIL_MUST_NOT_SURVIVE";
            var filler = new string('x', 200_000) + sentinel;
            foreach (var bundle in graph.Context.Bundles.Values)
            {
                await File.WriteAllTextAsync(Path.Combine(outdir, bundle.GetFileName()), filler);
            }

            var writer = new DiskResultWriter(graph.Context, outdir);
            var emitted = await writer.WriteOut(options);

            Assert.NotEmpty(emitted);
            foreach (var file in emitted)
            {
                var path = Path.Combine(outdir, file.Name);
                var bytes = await File.ReadAllBytesAsync(path);

                // The file on disk must be exactly what was written this build —
                // same length as reported, and free of any previous-build bytes.
                Assert.Equal(file.Size, bytes.Length);
                Assert.DoesNotContain(sentinel, await File.ReadAllTextAsync(path));
            }
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            Directory.Delete(outdir, recursive: true);
        }
    }
}
