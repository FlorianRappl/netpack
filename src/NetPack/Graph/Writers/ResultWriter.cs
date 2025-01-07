namespace NetPack.Graph.Writers;

abstract class ResultWriter(BundlerContext context)
{
    protected readonly BundlerContext _context = context;

    public event EventHandler? Started;
    public event EventHandler? Finished;

    public async Task WriteOut(OutputOptions options)
    {
        Started?.Invoke(this, EventArgs.Empty);

        await Parallel.ForEachAsync(_context.Assets, async (asset, ct) =>
        {
            var fn = asset.GetFileName();
            using var dst = OpenWrite(fn);
            using var src = await asset.CreateStream(options);
            await src.CopyToAsync(dst, ct);
            CloseWrite(fn, dst);
        });

        await Parallel.ForEachAsync(_context.Bundles, async (bundle, ct) =>
        {
            var fn = bundle.GetFileName();
            using var dst = OpenWrite(fn);
            using var src = await bundle.CreateStream(options);
            await src.CopyToAsync(dst, ct);
            CloseWrite(fn, dst);
        });
        
        Finished?.Invoke(this, EventArgs.Empty);
    }

    protected abstract Stream OpenWrite(string name);

    protected virtual void CloseWrite(string name, Stream stream)
    {
    }
}
