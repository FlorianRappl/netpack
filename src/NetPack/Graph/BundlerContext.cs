namespace NetPack.Graph;

using System.Collections.Concurrent;

public sealed class BundlerContext
{
    public ConcurrentBag<Asset> Assets = [];

    public ConcurrentBag<Bundle> Bundles = [];

    public ConcurrentBag<Dependency> Dependencies = [];

    public ConcurrentDictionary<string, Node> Modules = [];

    public async Task WriteOut(string target, bool optimize)
    {
        Directory.CreateDirectory(target);

        await Parallel.ForEachAsync(Assets, async (asset, ct) =>
        {
            var fileName = Path.Combine(target, asset.GetFileName());
            using var dst = File.OpenWrite(fileName);
            using var src = await asset.CreateStream(optimize);
            await src.CopyToAsync(dst, ct);
        });

        await Parallel.ForEachAsync(Bundles, async (bundle, ct) =>
        {
            var fileName = Path.Combine(target, bundle.GetFileName());
            using var dst = File.OpenWrite(fileName);
            using var src = await bundle.CreateStream(optimize);
            await src.CopyToAsync(dst, ct);
        });
    }
}
