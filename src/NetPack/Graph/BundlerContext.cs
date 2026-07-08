namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Fragments;
using NetPack.Graph.Bundles;

public sealed class BundlerContext(string root, FeatureFlags features, ModuleIdMap? moduleIds = null)
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

    /// <summary>
    /// The printed (pre-mangle) source of each module's factory, keyed by module
    /// id. Populated during a reloading build so the dev server can diff modules
    /// between compiles and push hot updates. Fresh per compile.
    /// </summary>
    public ConcurrentDictionary<int, string> ModuleFactories = [];

    private readonly ModuleIdMap _moduleIds = moduleIds ?? new ModuleIdMap();

    /// <summary>
    /// Returns a compact, stable integer id for a module node, keyed by file
    /// name. Shared across every bundle (the context is shared) and — when the
    /// dev server passes a persistent <see cref="ModuleIdMap"/> — stable across
    /// recompiles so hot updates can address a loaded module by id.
    /// </summary>
    public int GetModuleId(Node node) => _moduleIds.Get(node.FileName);
}
