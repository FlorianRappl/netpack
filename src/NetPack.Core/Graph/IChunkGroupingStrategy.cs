namespace NetPack.Graph;

/// <summary>
/// A chunk grouping strategy decides how modules are partitioned into bundles
/// after the dependency graph is built. Implementations receive the full graph
/// context and return a mapping from chunk root nodes to their constituent
/// module nodes — the same shape <see cref="Connected.Apply"/> returns.
/// </summary>
public interface IChunkGroupingStrategy
{
    IDictionary<Node, HashSet<Node>> GroupChunks(IEnumerable<Node> entryNodes, BundlerContext context);
}
