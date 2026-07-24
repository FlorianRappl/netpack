namespace NetPack.Graph.Writers;

using System.Collections.Concurrent;
using NetPack.Graph.Bundles;

abstract class ResultWriter(BundlerContext context)
{
    protected readonly BundlerContext _context = context;

    public event EventHandler? Started;
    public event EventHandler? Finished;

    /// <summary>
    /// Renders every asset and bundle and returns a report of what was emitted
    /// (file name, byte size and, for bundles, module count) so callers can print
    /// a build summary.
    /// </summary>
    public async Task<IReadOnlyList<EmittedFile>> WriteOut(OutputOptions options)
    {
        Started?.Invoke(this, EventArgs.Empty);

        if (options.IsOptimizing)
        {
            // Whole-program tree-shaking: prune dead code and unreferenced,
            // side-effect-free modules before anything is rendered.
            TreeShakePass.Run(_context);
        }

        await AssignHashedNames(options);

        var emitted = new ConcurrentBag<EmittedFile>();

        await Parallel.ForEachAsync(_context.Assets.Values, async (asset, ct) =>
        {
            var fn = asset.GetFileName();
            using var dst = OpenWrite(fn);
            using var src = await asset.CreateStream(options);
            await src.CopyToAsync(dst, ct);
            var size = src.CanSeek ? src.Length : dst.CanSeek ? dst.Length : 0;
            CloseWrite(fn, dst);
            emitted.Add(new EmittedFile(fn, size, Modules: 0, IsBundle: false));
        });

        await Parallel.ForEachAsync(_context.Bundles.Values, async (bundle, ct) =>
        {
            var fn = bundle.GetFileName();
            long size;

            using (var dst = OpenWrite(fn))
            {
                using var src = await bundle.CreateStream(options);
                await src.CopyToAsync(dst, ct);
                size = src.CanSeek ? src.Length : dst.CanSeek ? dst.Length : 0;
                CloseWrite(fn, dst);
            }

            emitted.Add(new EmittedFile(fn, size, bundle.Items.Length, IsBundle: true));

            // A JS bundle may have produced a companion source map.
            if (bundle.SourceMap is { } map)
            {
                var mapName = $"{fn}.map";
                using var mapDst = OpenWrite(mapName);
                await mapDst.WriteAsync(map, ct);
                CloseWrite(mapName, mapDst);
                emitted.Add(new EmittedFile(mapName, map.Length, Modules: 0, IsBundle: false));
            }
        });

        Finished?.Invoke(this, EventArgs.Empty);
        return emitted.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// When the naming template requests a content hash, renders every JS/CSS
    /// bundle once to hash its output, then assigns hashed <see cref="Bundle.OutputName"/>s
    /// so the real render (and every reference to a bundle) uses the hashed name.
    /// Hashes are computed against the un-hashed references uniformly (a two-phase
    /// pass), so the result is deterministic. The entry HTML keeps its own name.
    /// Note: a hash reflects a bundle's own content, so a change confined to a
    /// referenced (e.g. shared) bundle re-hashes that bundle but not its
    /// dependents.
    /// </summary>
    private async Task AssignHashedNames(OutputOptions options)
    {
        if (!options.EntryNames.Contains("[hash]", StringComparison.Ordinal))
        {
            return;
        }

        var hashes = new List<(Bundle Bundle, string Hash)>();

        foreach (var bundle in _context.Bundles.Values)
        {
            // The HTML document is the stable entry point; leave its name alone
            // (it still picks up the hashed names of the bundles it references).
            if (bundle.Type == ".html")
            {
                continue;
            }

            using var buffer = new MemoryStream();

            using (var src = await bundle.CreateStream(options))
            {
                await src.CopyToAsync(buffer);
            }

            hashes.Add((bundle, await Hash.ComputeHash(buffer)));
        }

        foreach (var (bundle, hash) in hashes)
        {
            bundle.AssignOutputName(options.EntryNames, hash);
        }
    }

    protected abstract Stream OpenWrite(string name);

    protected virtual void CloseWrite(string name, Stream stream)
    {
    }
}
