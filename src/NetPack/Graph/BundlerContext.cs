namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Fragments;
using NetPack.Graph.Bundles;

public sealed class BundlerContext(string root, FeatureFlags features)
{
    public string Root => root;

    public FeatureFlags Features => features;

    public ConcurrentDictionary<Node, Asset> Assets = [];

    public ConcurrentDictionary<Node, Bundle> Bundles = [];

    public ConcurrentBag<Dependency> Dependencies = [];

    public ConcurrentDictionary<string, Node> Modules = [];

    public ConcurrentDictionary<string, string> Aliases = [];

    public ConcurrentBag<string> Externals = [];

    public ConcurrentBag<string> Shared = [];

    public ConcurrentDictionary<Node, HtmlFragment> HtmlFragments = [];

    public ConcurrentDictionary<Node, JsFragment> JsFragments = [];

    public ConcurrentDictionary<Node, CssFragment> CssFragments = [];
}
