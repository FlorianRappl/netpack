namespace NetPack.Graph.Writers;

using System.Collections.Concurrent;

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

    protected abstract Stream OpenWrite(string name);

    protected virtual void CloseWrite(string name, Stream stream)
    {
    }
}
