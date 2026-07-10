namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Fragments;
using NetPack.Graph.Bundles;

public sealed class BundlerContext(string root, FeatureFlags features, ModuleIdMap? moduleIds = null)
{
    public string Root => root;

    public FeatureFlags Features => features;

    /// <summary>
    /// Project-wide JSX factory from <c>tsconfig.json</c>
    /// (<c>compilerOptions.jsxFactory</c>), applied to TypeScript source files
    /// unless a file overrides it with a local <c>@jsx</c> pragma. Null when no
    /// tsconfig is found or the option is unset.
    /// </summary>
    public string? JsxFactory { get; set; }

    /// <summary>
    /// Project-wide JSX fragment factory from <c>tsconfig.json</c>
    /// (<c>compilerOptions.jsxFragmentFactory</c>). See <see cref="JsxFactory"/>.
    /// </summary>
    public string? JsxFragmentFactory { get; set; }

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

    private readonly object _usageLock = new();
    private Dictionary<Node, NetPack.Syntax.Optimizer.UsedExports>? _exportUsage;

    /// <summary>
    /// Returns which exports of <paramref name="node"/> the program uses, for
    /// tree-shaking. Computed once for the whole graph and cached. Unknown
    /// modules fall back to "everything used" (safe).
    /// </summary>
    public NetPack.Syntax.Optimizer.UsedExports GetUsedExports(Node node)
    {
        if (_exportUsage is null)
        {
            lock (_usageLock)
            {
                _exportUsage ??= ExportUsage.Compute(this);
            }
        }
        return _exportUsage.TryGetValue(node, out var used)
            ? used
            : NetPack.Syntax.Optimizer.UsedExports.Everything();
    }

    /// <summary>
    /// Returns a compact, stable integer id for a module node, keyed by file
    /// name. Shared across every bundle (the context is shared) and — when the
    /// dev server passes a persistent <see cref="ModuleIdMap"/> — stable across
    /// recompiles so hot updates can address a loaded module by id.
    /// </summary>
    public int GetModuleId(Node node) => _moduleIds.Get(node.FileName);
}
