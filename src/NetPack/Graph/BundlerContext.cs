namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Fragments;
using NetPack.Graph.Bundles;

public sealed class BundlerContext
{
    public ConcurrentDictionary<Node, Asset> Assets = [];

    public ConcurrentDictionary<Node, Bundle> Bundles = [];

    public ConcurrentBag<Dependency> Dependencies = [];

    public ConcurrentDictionary<string, Node> Modules = [];

    public ConcurrentBag<string> Externals = [];

    public ConcurrentBag<string> Shared = [];

    public ConcurrentDictionary<Node, HtmlFragment> HtmlFragments = [];

    public ConcurrentDictionary<Node, JsFragment> JsFragments = [];

    public ConcurrentDictionary<Node, CssFragment> CssFragments = [];
}
