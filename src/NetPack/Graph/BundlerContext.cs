namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Assets;

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
            var processor = AssetProcessorFactory.GetProcessor(asset.Type);
            using var src = await processor.ProcessAsync(asset, optimize);
            using var dst = File.OpenWrite(fileName);
            await src.CopyToAsync(dst, ct);
        });

        var nodes = Bundles.Select(m => m.Root);
        var graphs = Connected.FindIndependentGraphs(nodes);

        await Parallel.ForEachAsync(graphs, async (graph, ct) =>
        {
            var name = Path.GetFileNameWithoutExtension(graph.Key);
            var ext = Helpers.GetType(Path.GetExtension(graph.Key));
            var fileName = Path.Combine(target, $"{name}{ext}");
            using var dst = File.OpenWrite(fileName);
            
            foreach (var node in graph.Value)
            {
                using var src = File.OpenRead(node.FileName);
                await src.CopyToAsync(dst, ct);
            }
        });
    }
}
