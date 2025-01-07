namespace NetPack.Graph;

abstract class ResultWriter(BundlerContext context)
{
    private readonly BundlerContext _context = context;

    public async Task WriteOut(bool optimize)
    {
        await Parallel.ForEachAsync(_context.Assets, async (asset, ct) =>
        {
            using var dst = OpenWrite(asset.GetFileName());
            using var src = await asset.CreateStream(optimize);
            await src.CopyToAsync(dst, ct);
        });

        await Parallel.ForEachAsync(_context.Bundles, async (bundle, ct) =>
        {
            using var dst = OpenWrite(bundle.GetFileName());
            using var src = await bundle.CreateStream(optimize);
            await src.CopyToAsync(dst, ct);
        });
    }

    protected abstract Stream OpenWrite(string name);
}
