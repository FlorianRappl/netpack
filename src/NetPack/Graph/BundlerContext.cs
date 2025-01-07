namespace NetPack.Graph;

using System.Collections.Concurrent;

public sealed class BundlerContext
{
    public ConcurrentBag<Asset> Assets = [];

    public ConcurrentBag<Bundle> Bundles = [];

    public ConcurrentBag<Dependency> Dependencies = [];

    public ConcurrentDictionary<string, Node> Modules = [];

    public ConcurrentBag<string> Externals = [];
}
