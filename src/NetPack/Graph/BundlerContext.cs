namespace NetPack.Graph;

using System.Collections.Concurrent;
using System.Text;
using NetPack.Assets;
using NetPack.Chunks;

public sealed class BundlerContext
{
    public ConcurrentBag<Asset> Assets = [];

    public ConcurrentBag<Bundle> Bundles = [];

    public ConcurrentBag<Dependency> Dependencies = [];

    public ConcurrentDictionary<Node, IChunk> Chunks = [];

    public ConcurrentDictionary<string, Node> Modules = [];

    public async Task WriteOut(string target, bool optimize)
    {
        Directory.CreateDirectory(target);

        await Parallel.ForEachAsync(Assets, async (asset, ct) =>
        {
            var fileName = Path.Combine(target, asset.GetFileName());
            var processor = AssetProcessorFactory.GetProcessor(asset.Type);
            using var src = await processor.ProcessAsync(asset, optimize);
            using var dst = File.OpenWrite(fileName);
            await src.CopyToAsync(dst, ct);
        });

        await Parallel.ForEachAsync(Bundles, async (bundle, ct) =>
        {
            var root = bundle.Root;
            var name = Path.GetFileNameWithoutExtension(root.FileName);
            var ext = Helpers.GetType(Path.GetExtension(root.FileName));
            var fileName = Path.Combine(target, $"{name}{ext}");
            using var dst = File.OpenWrite(fileName);
            
            if (Chunks.TryGetValue(bundle.Root, out var chunk))
            {
                var content = chunk.Stringify(this, optimize);
                using var src = new MemoryStream();
                var raw = Encoding.UTF8.GetBytes(content);
                await src.WriteAsync(raw, ct);
                src.Position = 0;
                await src.CopyToAsync(dst, ct);
            }
        });
    }
}
