namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Fragments;
using NetPack.Graph.Bundles;

public sealed class BundlerContext(string root, FeatureFlags features, ModuleIdMap? moduleIds = null)
{
    public string Root => root;

    public FeatureFlags Features => features;

    /// <summary>The target runtime, which decides the built-in modules that stay
    /// external and how dependency entry points are chosen. Defaults to the web.</summary>
    internal PlatformTarget Platform { get; set; } = new WebPlatform();

    /// <summary>
    /// True when React Fast Refresh instrumentation is enabled for this build
    /// (dev server + <c>react-refresh</c> resolvable).
    /// </summary>
    public bool ReactRefresh { get; set; }

    /// <summary>The bundled <c>react-refresh/runtime</c> module, when Fast Refresh
    /// is enabled.</summary>
    public Node? ReactRefreshRuntime { get; set; }

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

    /// <summary>
    /// Project-wide default JSX factory selected from dependency heuristics
    /// (e.g. Preact when <c>preact</c> is installed and <c>react</c> is not).
    /// Applied when neither a file-local pragma nor tsconfig override is set.
    /// </summary>
    public string? DefaultJsxFactory { get; set; }

    /// <summary>
    /// Fragment counterpart to <see cref="DefaultJsxFactory"/>.
    /// </summary>
    public string? DefaultJsxFragmentFactory { get; set; }

    /// <summary>
    /// Optional module specifier to auto-import for JSX files using the default
    /// runtime (for example, <c>preact</c> when using <c>Preact.h</c>).
    /// </summary>
    public string? DefaultJsxImportModule { get; set; }

    /// <summary>
    /// Local identifier name used for <see cref="DefaultJsxImportModule"/>.
    /// </summary>
    public string? DefaultJsxImportIdentifier { get; set; }

    public ConcurrentDictionary<Node, Asset> Assets = [];

    public ConcurrentDictionary<Node, Bundle> Bundles = [];

    public ConcurrentBag<Dependency> Dependencies = [];

    public ConcurrentDictionary<string, Node> Modules = [];

    public ConcurrentDictionary<string, string> Aliases = [];

    /// <summary>
    /// Compile-time constant substitutions applied to JS/TS source before
    /// parsing (the <c>--define</c> option), e.g. <c>process.env.NODE_ENV</c> →
    /// <c>"production"</c>. Ordered longest-key-first so a more specific key
    /// (<c>process.env.NODE_ENV</c>) is replaced before a shorter overlapping one
    /// (<c>process</c>). Each value is the raw replacement text (already a valid
    /// JS expression, so string constants include their quotes).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Defines { get; set; } = [];

    /// <summary>
    /// Per-extension loader overrides (the <c>--loader</c> option), keyed by a
    /// lowercase extension including the leading dot (e.g. <c>.svg</c> →
    /// <c>text</c>). Decides how a file of that type is turned into a module or
    /// asset, overriding netpack's built-in handling.
    /// </summary>
    public IReadOnlyDictionary<string, string> Loaders { get; set; } = new Dictionary<string, string>();

    public ConcurrentBag<string> Externals = [];

    public ConcurrentBag<string> Shared = [];

    public ConcurrentDictionary<Node, HtmlFragment> HtmlFragments = [];

    public ConcurrentDictionary<Node, JsFragment> JsFragments = [];

    public ConcurrentDictionary<Node, CssFragment> CssFragments = [];

    /// <summary>
    /// CSS files imported from a JS/TS module, mapped to the bundle that should
    /// own the generated virtual module. Such CSS nodes are converted into JS
    /// modules (runtime style injection + class-name exports) before bundling.
    /// </summary>
    public ConcurrentDictionary<Node, Bundle> CssImports = [];

    /// <summary>
    /// The subset of <see cref="CssImports"/> imported with named/default
    /// bindings, i.e. treated as CSS modules (class names are hashed).
    /// </summary>
    public ConcurrentDictionary<Node, bool> CssModuleNodes = [];

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
