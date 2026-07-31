namespace NetPack.Graph;

/// <summary>
/// Default chunk grouping strategy that delegates to <see cref="Connected"/>
/// for backward compatibility. Uses the existing shared-module extraction
/// heuristic: any module reachable from multiple entry points is moved into
/// a shared <c>common.NNNN.{ext}</c> chunk.
/// </summary>
public class DefaultChunkStrategy : IChunkGroupingStrategy
{
    public IDictionary<Node, HashSet<Node>> GroupChunks(IEnumerable<Node> entryNodes, BundlerContext context)
    {
        var connected = new Connected((i, nodes) => $"common.{i:0000}{nodes.First().Type}");
        return connected.Apply(entryNodes);
    }
}
