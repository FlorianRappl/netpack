namespace NetPack.Graph;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Css.Parser;
using AngleSharp.Text;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using NetPack.Graph.Visitors;
using NetPack.Graph.Writers;
using NetPack.Json;
using NetPack.Syntax;
using static NetPack.Helpers;

public class Traverse(string root, FeatureFlags features, ModuleIdMap? moduleIds = null, DirectoryListingCache? directoryFiles = null, BuildCache? buildCache = null) : IDisposable
{
    private readonly BundlerContext _context = new(root, features, moduleIds);
    private readonly BrowsingContext _browser = new(Configuration.Default.WithCss());
    // Lazy<Task<Node>> — not Task<Node> — so the node is built exactly once per key.
    // ConcurrentDictionary.GetOrAdd may run its value factory more than once under
    // contention; wrapping in Lazy guarantees AddNewNodeToBundle (which has side
    // effects: creating the node, bundle and fragment) runs a single time, so two
    // importers of the same module always share one node (a precondition for
    // shared-chunk detection).
    private readonly ConcurrentDictionary<string, Lazy<Task<Node>>> _reserved = [];
    private readonly DirectoryListingCache? _directoryFiles = directoryFiles;
    private readonly BuildCache? _buildCache = buildCache;
    private readonly NodeJs _njs = new(root);
    private bool _devServer;
    private bool _quiet;
    private int _nextPostOrderIndex;

    private async Task<string> TranspileSass(string content, string file)
    {
        var result = await _njs.RunCommand("sass", content, file);
        var sass = result.Deserialize(SourceGenerationContext.Default.SassCommandResult);
        return sass?.Css ?? "";
    }

    private async Task<string> TranspileLess(string content, string file)
    {
        var result = await _njs.RunCommand("less", content, file);
        var sass = result.Deserialize(SourceGenerationContext.Default.SassCommandResult);
        return sass?.Css ?? "";
    }

    private async Task<string> TranspilePostCss(string content, string file)
    {
        var result = await _njs.RunCommand("postcss", content, file);
        var sass = result.Deserialize(SourceGenerationContext.Default.SassCommandResult);
        return sass?.Css ?? "";
    }

    private async Task<string> TranspileCodegen(string file)
    {
        var result = await _njs.RunCommand("codegen", file);
        return result.Deserialize(SourceGenerationContext.Default.String) ?? "";
    }

    public BundlerContext Context => _context;

    public static Task<Traverse> From(string path) => From(path, [], []);

    public static async Task<Traverse> From(string path, IEnumerable<string> externals, IEnumerable<string> shared, ModuleIdMap? moduleIds = null, bool devServer = false, Platform platform = Platform.Web, IReadOnlyDictionary<string, string>? defines = null, IReadOnlyDictionary<string, string>? aliases = null, IReadOnlyDictionary<string, string>? loaders = null, IEnumerable<string>? conditions = null, bool externalPackages = false, string? mode = null, IReadOnlyDictionary<string, string>? envVars = null, DirectoryListingCache? directoryFiles = null, BuildCache? buildCache = null, CodegenCache? codegenCache = null, RenderCache? renderCache = null, PassContext? passContext = null, BuildSnapshot? snapshot = null, NetPack.Config.SplitChunksConfig? splitChunks = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? hookModules = null, bool quiet = false)
    {
        var root = Path.GetDirectoryName(path)!;
        var packageRoot = FindRoot(root);
        var features = await FindFeatures(packageRoot);
        var traverse = new Traverse(packageRoot ?? root, features, moduleIds, directoryFiles, buildCache) { _devServer = devServer, _quiet = quiet };
        traverse.Context.CodegenCache = codegenCache;
        traverse.Context.RenderCache = renderCache;
        traverse.Context.PassContext = passContext;
        traverse.Context.Snapshot = snapshot;
        traverse.Context.SplitChunks = splitChunks;
        traverse.Context.Platform = PlatformTargets.For(platform);
        traverse.Context.Defines = BuildDefines(defines, devServer, mode);
        traverse.Context.EnvVars = envVars ?? new Dictionary<string, string>();
        traverse.Context.Loaders = NormalizeLoaders(loaders);
        traverse.Context.UserConditions = conditions is null ? [] : [.. conditions];
        traverse.Context.ExternalPackages = externalPackages;
        ApplyAliases(traverse.Context, aliases);
        var (jsxFactory, jsxFragmentFactory) = await FindJsxFactories(packageRoot);
        var (defaultJsxFactory, defaultJsxFragmentFactory, defaultJsxImportModule, defaultJsxImportIdentifier) = await FindDefaultJsxRuntime(packageRoot);
        traverse.Context.JsxFactory = jsxFactory;
        traverse.Context.JsxFragmentFactory = jsxFragmentFactory;
        traverse.Context.DefaultJsxFactory = defaultJsxFactory;
        traverse.Context.DefaultJsxFragmentFactory = defaultJsxFragmentFactory;
        traverse.Context.DefaultJsxImportModule = defaultJsxImportModule;
        traverse.Context.DefaultJsxImportIdentifier = defaultJsxImportIdentifier;
        traverse.Context.UseSolid = await FindSolidRuntime(packageRoot);
        traverse.Context.Externals = [.. externals, .. shared];
        traverse.Context.Shared = [.. shared];

        // Register preset hooks as taps on the build's hook containers, executed
        // over this instance's Node bridge. Only when there are hooks — a hook-less
        // build leaves Context.Hooks null and never touches the bridge for hooks.
        if (hookModules is { Count: > 0 })
        {
            var buildHooks = new NetPack.Plugins.BuildHooks();
            NetPack.Plugins.PresetHooks.Bind(buildHooks, hookModules, new NetPack.Plugins.NodeHookRunner(traverse._njs), traverse.Context.Root);
            traverse.Context.Hooks = buildHooks;
        }

        await traverse.Run([path, .. shared]);
        return traverse;
    }

    /// <summary>
    /// Builds the effective <c>--define</c> table: the built-in
    /// <c>process.env.NODE_ENV</c> default (development on the dev server,
    /// production otherwise) overlaid with the user's entries, then ordered
    /// longest-key-first for safe sequential text replacement. A non-empty
    /// <c>mode</c> overrides the dev-server/production default.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string>> BuildDefines(IReadOnlyDictionary<string, string>? defines, bool devServer, string? mode)
    {
        var defaultMode = !string.IsNullOrEmpty(mode)
            ? mode
            : devServer ? "development" : "production";

        var map = new Dictionary<string, string>
        {
            ["process.env.NODE_ENV"] = $"'{defaultMode}'",
        };

        if (defines is not null)
        {
            foreach (var (key, value) in defines)
            {
                map[key] = value;
            }
        }

        return [.. map.OrderByDescending(kv => kv.Key.Length)];
    }

    private static IReadOnlyDictionary<string, string> NormalizeLoaders(IReadOnlyDictionary<string, string>? loaders)
    {
        var map = new Dictionary<string, string>();

        if (loaders is not null)
        {
            foreach (var (extension, loader) in loaders)
            {
                var key = extension.StartsWith('.') ? extension : "." + extension;
                map[key.ToLowerInvariant()] = loader.ToLowerInvariant();
            }
        }

        return map;
    }

    private static void ApplyAliases(BundlerContext context, IReadOnlyDictionary<string, string>? aliases)
    {
        if (aliases is null)
        {
            return;
        }

        foreach (var (from, to) in aliases)
        {
            // A path target (relative or absolute) resolves from the working
            // directory so it is importer-independent; a bare specifier is left
            // as-is to go through normal package resolution.
            var target = to.StartsWith('.') || Path.IsPathRooted(to)
                ? CombinePath(Environment.CurrentDirectory, to)
                : to;
            context.Aliases[from] = target;
        }
    }

    private async Task Run(params IEnumerable<string> entryPoints)
    {
        // Compiler + compilation lifecycle hooks fire before any module is built.
        FireSync(_context.Hooks?.Compiler.Initialize);
        await FireAsync(_context.Hooks?.Compiler.BeforeRun);
        await FireAsync(_devServer ? _context.Hooks?.Compiler.WatchRun : _context.Hooks?.Compiler.Run);
        await FireAsync(_context.Hooks?.Compiler.BeforeCompile);
        FireSync(_context.Hooks?.Compiler.Compile);
        FireSync(_context.Hooks?.Compiler.ThisCompilation);
        await FireAsync(_context.Hooks?.Compiler.Compilation);
        await FireAsync(_context.Hooks?.Compiler.Make);

        var pc = _context.PassContext;
        if (pc is not null)
        {
            pc.Store(IncrementalPass.BuildModuleGraph, "started", true);
        }

        var queue = new List<Task>();
        Node? primaryEntry = null;

        foreach (var entryPoint in entryPoints)
        {
            var entry = await Resolve(_context.Root, entryPoint);
            var name = Path.GetFileName(entry);

            switch (name)
            {
                // special case - Module / Native Federation
                case "federation.json":
                    await AddFederation(entry);
                    break;
                default:
                    var node = await AddNewBundle(entry);
                    primaryEntry ??= node;
                    break;
            }
        }

        await Task.WhenAll(queue);

        // CSS code splitting: compute shared CSS and create separate chunks
        var cssSplitter = new CssChunkSplitter(_context);
        var sharedCss = cssSplitter.ComputeSharedCss();
        cssSplitter.CreateSharedCssBundles(sharedCss);

        // Detect ordering conflicts among shared CSS modules
        DetectCssOrderConflicts();

        await TransformCssModules(sharedCss);

        if (_devServer && primaryEntry is not null)
        {
            await SetupReactRefresh(primaryEntry);
        }

        if (pc is not null)
        {
            pc.Store(IncrementalPass.BuildModuleGraph, "completed", _context.Modules.Count);
        }

        await FireAsync(_context.Hooks?.Compiler.FinishMake);
        await FireAsync(_context.Hooks?.Compilation.FinishModules);

        Finish();
    }

    // -- hook firing helpers -----------------------------------------------

    private NetPack.Plugins.CompilerContext CompilerHookContext() => new() { IsDevelopment = _devServer };

    private NetPack.Plugins.CompilationContext CompilationHookContext()
        => new() { BundlerContext = _context, IsDevelopment = _devServer };

    private Task FireAsync(NetPack.Plugins.SeriesHook<NetPack.Plugins.CompilerContext>? hook)
        => hook is null || hook.Count == 0 ? Task.CompletedTask : hook.CallAsync(CompilerHookContext());

    private Task FireAsync(NetPack.Plugins.SeriesHook<NetPack.Plugins.CompilationContext>? hook)
        => hook is null || hook.Count == 0 ? Task.CompletedTask : hook.CallAsync(CompilationHookContext());

    private void FireSync(NetPack.Plugins.SyncHook<NetPack.Plugins.CompilerContext>? hook)
    {
        if (hook is not null && hook.Count > 0)
        {
            hook.Call(CompilerHookContext());
        }
    }

    private void FireSync(NetPack.Plugins.SyncHook<NetPack.Plugins.CompilationContext>? hook)
    {
        if (hook is not null && hook.Count > 0)
        {
            hook.Call(CompilationHookContext());
        }
    }

    private Task FireModuleAsync(NetPack.Plugins.SeriesHook<NetPack.Plugins.ModuleBuildContext>? hook, Node module)
        => hook is null || hook.Count == 0
            ? Task.CompletedTask
            : hook.CallAsync(new NetPack.Plugins.ModuleBuildContext
            {
                BundlerContext = _context,
                Module = module,
                IsDevelopment = _devServer,
            });

    /// <summary>
    /// Enables React Fast Refresh when the project has <c>react-refresh</c>
    /// installed: bundles its runtime and flags the context so the JS bundle
    /// instruments component modules. A no-op (normal HMR) when the package is
    /// absent.
    /// </summary>
    private async Task SetupReactRefresh(Node entryNode)
    {
        var runtimePath = await ResolveFromNodeModules(_context.Root, "react-refresh/runtime");

        if (runtimePath is null || !_context.Bundles.TryGetValue(entryNode, out var bundle) || bundle is not JsBundle)
        {
            return;
        }

        var runtimeNode = await AddToBundle(bundle, runtimePath);
        entryNode.Children.Add(runtimeNode);
        _context.ReactRefresh = true;
        _context.ReactRefreshRuntime = runtimeNode;
    }

    private void Finish()
    {
        var pc = _context.PassContext;
        if (pc is not null)
        {
            pc.Store(IncrementalPass.FinishModules, "started", true);
        }

        // Assign deterministic post-order indices by running a DFS that follows
        // Children in sorted order. This runs after the full graph is built,
        // so it is independent of the non-deterministic resolution order during
        // parallel import processing.
        AssignPostOrderIndices();

        var bundles = _context.Bundles;
        var strategy = ChunkStrategyFactory.Create(_context.SplitChunks);
        var graphs = strategy.GroupChunks(bundles.Keys, _context);

        foreach (var graph in graphs)
        {
            if (!bundles.TryGetValue(graph.Key, out var bundle))
            {
                bundle = CreateBundle(graph.Key, BundleFlags.Shared);
                bundles.TryAdd(graph.Key, bundle);
            }

            // Sort modules by post-order index so the bundle factory registry
            // emits them in declaration / evaluation order, which is the
            // foundation for deterministic CSS ordering.
            bundle.Items = [.. graph.Value.OrderBy(n => n.PostOrderIndex)];
        }

        if (_context.PassContext is not null)
        {
            _context.PassContext.Store(IncrementalPass.FinishModules, "completed", _context.Bundles.Count);
            _context.PassContext.Store(IncrementalPass.BuildChunkGraph, "completed", _context.Bundles.Count);
        }
    }

    /// <summary>
    /// Assigns deterministic post-order indices to all modules by walking the
    /// graph from each entry point in source order. Children are sorted by the
    /// declared import position within their parent's AST body (recorded during
    /// parsing), ensuring CSS files that appear earlier in import lists get
    /// lower post-order indices.
    /// </summary>
    private void AssignPostOrderIndices()
    {
        _nextPostOrderIndex = 0;
        var seen = new HashSet<Node>();

        foreach (var bundle in _context.Bundles.Values.Where(b => b.IsPrimary))
        {
            WalkPostOrder(bundle.Root, seen);
        }

        foreach (var bundle in _context.Bundles.Values)
        {
            WalkPostOrder(bundle.Root, seen);
        }
    }

    /// <summary>
    /// Iterative post-order DFS using a manual stack. Walks Children in sorted
    /// order for deterministic indices without risking stack overflow on deep
    /// import chains or cyclic graphs.
    /// </summary>
    private void WalkPostOrder(Node root, HashSet<Node> seen)
    {
        if (!seen.Add(root)) return;

        // Stack entries: (node, enumerator over children, state)
        // state 0 = first visit (need to push children), 1 = children done
        var stack = new Stack<(Node Node, IEnumerator<Node> Enumerator, int State)>();
        stack.Push((root, root.Children.OrderBy(n => n.FileName, StringComparer.Ordinal).GetEnumerator(), 0));

        while (stack.Count > 0)
        {
            var (node, enumerator, state) = stack.Peek();

            if (state == 0)
            {
                // First visit: descend into next unseen child
                while (enumerator.MoveNext())
                {
                    var child = enumerator.Current;
                    if (seen.Add(child))
                    {
                        var childEnumerator = child.Children
                            .OrderBy(n => n.FileName, StringComparer.Ordinal)
                            .GetEnumerator();
                        stack.Push((child, childEnumerator, 0));
                        goto next;
                    }
                }

                // All children processed, mark for post-order assignment
                stack.Pop();
                stack.Push((node, enumerator, 1));
            }
            else
            {
                // Post-order: all children are done
                node.PostOrderIndex = _nextPostOrderIndex++;
                stack.Pop();
            }

            next:;
        }
    }

    private Bundle CreateBundle(Node root, BundleFlags flags)
    {
        if (TryCreateBundle(root, flags, out var bundle))
        {
            return bundle;
        }

        throw new NotSupportedException($"No bundle for type '{root.Type}' found.");
    }

    private bool TryCreateBundle(Node root, BundleFlags flags, [NotNullWhen(returnValue: true)] out Bundle? bundle)
    {
        switch (root.Type)
        {
            case ".html":
                bundle = new HtmlBundle(_context, root, flags);
                return true;
            case ".js":
            case ".codegen":
                bundle = new JsBundle(_context, root, flags);
                return true;
            case ".css":
                bundle = new CssBundle(_context, root, flags);
                return true;
            default:
                bundle = default;
                return false;
        }
    }

    private static string? FindRoot(string root)
    {
        var files = Directory.GetFiles(root);
        var packageJsonPath = Path.Combine(root, "package.json");

        if (files.Contains(packageJsonPath))
        {
            return root;
        }

        var parent = Directory.GetParent(root)?.FullName;

        if (parent is not null && parent != root)
        {
            return FindRoot(parent);
        }

        return null;
    }

    private static async Task<FeatureFlags> FindFeatures(string? root)
    {
        var features = FeatureFlags.None;

        if (root is not null)
        {
            var files = Directory.GetFiles(root);
            var packageJsonPath = Path.Combine(root, "package.json");
            var postCssPath = Path.Combine(root, "postcss.config.js");
            using var packageJson = File.OpenRead(packageJsonPath);
            var jsonDoc = await JsonDocument.ParseAsync(packageJson);
            var jsonObj = jsonDoc.RootElement;

            void Inspect(JsonElement element)
            {
                if (element.TryGetProperty("postcss", out _) && files.Contains(postCssPath))
                {
                    features |= FeatureFlags.PostCss;
                }

                if (element.TryGetProperty("sass", out _))
                {
                    features |= FeatureFlags.Sass;
                }

                if (element.TryGetProperty("less", out _))
                {
                    features |= FeatureFlags.Less;
                }
            }

            if (jsonObj.TryGetProperty("dependencies", out var dependencies))
            {
                Inspect(dependencies);
            }

            if (jsonObj.TryGetProperty("devDependencies", out var devDependencies))
            {
                Inspect(devDependencies);
            }
        }

        return features;
    }

    /// <summary>
    /// Reads the JSX factory options from a <c>tsconfig.json</c> at the project
    /// root, if present. Returns the <c>compilerOptions.jsxFactory</c> and
    /// <c>compilerOptions.jsxFragmentFactory</c> values (or null when unset). The
    /// file is parsed leniently (comments and trailing commas allowed, as
    /// tsconfig files commonly contain them).
    /// </summary>
    private static async Task<(string? Factory, string? FragmentFactory)> FindJsxFactories(string? root)
    {
        if (root is null)
        {
            return default;
        }

        var path = Path.Combine(root, "tsconfig.json");

        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            using var doc = await JsonDocument.ParseAsync(stream, options);

            if (doc.RootElement.TryGetProperty("compilerOptions", out var compilerOptions))
            {
                return (ReadString(compilerOptions, "jsxFactory"), ReadString(compilerOptions, "jsxFragmentFactory"));
            }
        }
        catch
        {
            // A malformed tsconfig shouldn't break the build; fall back to defaults.
        }

        return default;

        static string? ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Picks a default JSX runtime from dependencies when no explicit JSX
    /// factory is configured. If <c>preact</c> is present and <c>react</c> is
    /// absent, JSX lowers to <c>Preact.h</c>/<c>Preact.Fragment</c> and modules
    /// that use JSX automatically import <c>Preact</c> from <c>preact</c>.
    /// </summary>
    private static async Task<(string? Factory, string? FragmentFactory, string? ImportModule, string? ImportIdentifier)> FindDefaultJsxRuntime(string? root)
    {
        if (root is null)
        {
            return default;
        }

        var packageJsonPath = Path.Combine(root, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            return default;
        }

        try
        {
            using var packageJson = File.OpenRead(packageJsonPath);
            using var jsonDoc = await JsonDocument.ParseAsync(packageJson);
            var jsonObj = jsonDoc.RootElement;

            var hasReact = HasDependency(jsonObj, "react");
            var hasPreact = HasDependency(jsonObj, "preact");

            if (hasPreact && !hasReact)
            {
                return ("Preact.h", "Preact.Fragment", "preact", "Preact");
            }
        }
        catch
        {
            // A malformed package.json shouldn't break the build; fall back.
        }

        return default;

        static bool HasDependency(JsonElement rootElement, string name)
        {
            return Has(rootElement, "dependencies", name)
                || Has(rootElement, "devDependencies", name)
                || Has(rootElement, "peerDependencies", name)
                || Has(rootElement, "optionalDependencies", name);
        }

        static bool Has(JsonElement rootElement, string section, string name)
        {
            return rootElement.TryGetProperty(section, out var depObj)
                && depObj.ValueKind == JsonValueKind.Object
                && depObj.TryGetProperty(name, out _);
        }
    }

    /// <summary>
    /// Detects whether the project targets Solid.js: <c>solid-js</c> is a
    /// dependency and <c>react</c> is not. In that case JSX files are compiled
    /// with Solid's official transform (<c>babel-preset-solid</c>) over the Node
    /// bridge rather than netpack's <c>createElement</c> lowering.
    /// </summary>
    private static async Task<bool> FindSolidRuntime(string? root)
    {
        if (root is null)
        {
            return false;
        }

        var packageJsonPath = Path.Combine(root, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            return false;
        }

        try
        {
            using var packageJson = File.OpenRead(packageJsonPath);
            using var jsonDoc = await JsonDocument.ParseAsync(packageJson);
            var jsonObj = jsonDoc.RootElement;

            return HasDependency(jsonObj, "solid-js") && !HasDependency(jsonObj, "react");
        }
        catch
        {
            return false;
        }

        static bool HasDependency(JsonElement rootElement, string name)
            => Has(rootElement, "dependencies", name)
                || Has(rootElement, "devDependencies", name)
                || Has(rootElement, "peerDependencies", name)
                || Has(rootElement, "optionalDependencies", name);

        static bool Has(JsonElement rootElement, string section, string name)
            => rootElement.TryGetProperty(section, out var depObj)
                && depObj.ValueKind == JsonValueKind.Object
                && depObj.TryGetProperty(name, out _);
    }

    private async Task<string> Resolve(string dir, string name)
    {
        if (!name.StartsWith('.') && !Path.IsPathFullyQualified(name))
        {
            var result = await ResolveFromNodeModules(dir, name);

            if (result is not null)
            {
                return result;
            }
        }

        return ResolveFromFileSystem(CombinePath(dir, name)) ?? throw new Exception($"Could not find the module '{name}' in '{dir}'. Make sure the module is installed (npm install {name}).");
    }

    private string? ResolveFromFileSystem(string fn)
    {
        if (Directory.Exists(fn))
        {
            fn = CombinePath(fn, "index");
        }

        var directory = Path.GetDirectoryName(fn)!;
        var files = _directoryFiles is not null
            ? _directoryFiles.GetFiles(directory)
            : Directory.GetFiles(directory);

        if (!files.Contains(fn))
        {
            foreach (var extension in ExtensionMap.Keys)
            {
                var trial = $"{fn}{extension}";

                if (files.Contains(trial))
                {
                    return trial;
                }
            }

            return null;
        }

        return fn;
    }

    private async Task<string?> ResolveFromNodeModules(string? currentDir, string packageName)
    {
        var (package, subpath) = SplitPackageSpecifier(packageName);

        while (currentDir is not null)
        {
            // The package root is the directory that owns package.json; its
            // "exports" field (when present) is the authoritative resolver.
            var packageRoot = CombinePath(currentDir, "node_modules", package);
            var packageJsonPath = CombinePath(packageRoot, "package.json");

            if (File.Exists(packageJsonPath))
            {
                var dependency = await LoadDependency(packageJsonPath);

                if (dependency.HasExports)
                {
                    var exported = dependency.ResolveExport(subpath, _context.ActiveConditions);

                    if (exported is not null)
                    {
                        if (File.Exists(exported))
                        {
                            return exported;
                        }

                        // Rare: an exports target without an explicit extension.
                        var viaFs = ResolveFromFileSystem(exported);

                        if (viaFs is not null)
                        {
                            return viaFs;
                        }
                    }

                    // With "exports" present but the subpath unexported we do not
                    // fall through to legacy fields for this package — but keep
                    // walking up in case a shadowing copy higher in the tree does.
                }
                else if (subpath == ".")
                {
                    if (File.Exists(dependency.Entry))
                    {
                        return dependency.Entry;
                    }
                }
            }

            // Legacy filesystem resolution: subpaths of packages without
            // "exports", nested packages, and bare file references.
            var nodeModulesPath = CombinePath(currentDir, "node_modules", packageName);

            if (Directory.Exists(nodeModulesPath))
            {
                var subPackageJsonPath = CombinePath(nodeModulesPath, "package.json");

                if (File.Exists(subPackageJsonPath))
                {
                    var dependency = await LoadDependency(subPackageJsonPath);

                    if (File.Exists(dependency.Entry))
                    {
                        return dependency.Entry;
                    }
                }
                else
                {
                    var defaultIndexPath = CombinePath(nodeModulesPath, "index.js");

                    if (File.Exists(defaultIndexPath))
                    {
                        return defaultIndexPath;
                    }
                }
            }
            else if (File.Exists(nodeModulesPath))
            {
                return nodeModulesPath;
            }
            else if (Directory.Exists(Path.GetDirectoryName(nodeModulesPath)))
            {
                var result = ResolveFromFileSystem(nodeModulesPath);

                if (result is not null)
                {
                    return result;
                }
            }

            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Splits a bare specifier into its package name and an <c>exports</c>-style
    /// subpath. <c>"react"</c> → (<c>react</c>, <c>.</c>);
    /// <c>"react-dom/client"</c> → (<c>react-dom</c>, <c>./client</c>);
    /// <c>"@angular/common/http"</c> → (<c>@angular/common</c>, <c>./http</c>).
    /// </summary>
    private static (string Package, string Subpath) SplitPackageSpecifier(string specifier)
    {
        var segments = specifier.Split('/');
        var nameSegments = specifier.StartsWith('@') && segments.Length >= 2 ? 2 : 1;
        var package = string.Join('/', segments.Take(nameSegments));
        var rest = segments.Skip(nameSegments).ToArray();
        var subpath = rest.Length > 0 ? "./" + string.Join('/', rest) : ".";
        return (package, subpath);
    }

    private async Task<Dependency> LoadDependency(string packageJsonPath)
    {
        var dependency = _context.Dependencies.FirstOrDefault(m => m.Location == packageJsonPath);

        if (dependency is null)
        {
            using var packageJson = File.OpenRead(packageJsonPath);
            var jsonDoc = await JsonDocument.ParseAsync(packageJson);
            var jsonObj = jsonDoc.RootElement;

            dependency = new Dependency(packageJsonPath, jsonObj, _context.Platform.UseBrowserField);

            if (!_context.Dependencies.Any(m => m.Location == packageJsonPath))
            {
                _context.Dependencies.Add(dependency);
            }
        }

        return dependency;
    }

    private async Task<Node?> InnerProcess(Bundle? bundle, Node parent, string name, (int? Width, int? Height, string? Format) variant)
    {
        if (_context.Aliases.TryGetValue(name, out var alias))
        {
            return await InnerProcess(bundle, parent, alias, variant);
        }

        if (_context.Externals.Contains(name))
        {
            return AddExternalReference(parent, name);
        }

        // Runtime built-ins carrying an explicit scheme (`node:fs`, `npm:`/`jsr:` on
        // Deno) are unambiguous — always provided by the runtime, kept external
        // verbatim. Bare core names (e.g. `fs`) are intentionally NOT handled here:
        // they are resolved locally first (a local module/package of the same name
        // wins) and only canonicalized to `node:` further down if nothing is found.
        if (_context.Platform.IsExplicitBuiltin(name))
        {
            return AddExternalReference(parent, name);
        }

        if (name.StartsWith("//") || name.StartsWith("file:") || name.StartsWith("http:") || name.StartsWith("https:"))
        {
            // ignore URLs
            return null;
        }

        // With --packages=external, every bare (node_modules) import is kept
        // external — a relative or absolute path is still bundled as usual. A bare
        // Node core module is still canonicalized to the `node:` scheme.
        if (_context.ExternalPackages && !name.StartsWith('.') && !Path.IsPathRooted(name))
        {
            return AddExternalReference(parent, _context.Platform.BuiltinFallback(name) ?? name);
        }

        // Split off a trailing `?...` query string (irrelevant for locating the
        // file itself) and, from it, any `width=`/`height=`/`format=` params —
        // this is how a JS/TS import requests an image variant, e.g.
        // `import img from './logo.png?width=200&height=100&format=webp'`. An
        // explicitly passed-in `variant` (from an HTML <img> width/height
        // attribute or a CSS background-size) wins if both are somehow present.
        var (path, queryVariant, inlineOverride) = ParseVariantQuery(name);
        var width = variant.Width ?? queryVariant.Width;
        var height = variant.Height ?? queryVariant.Height;
        var format = variant.Format ?? queryVariant.Format;

        try
        {
            var file = await Resolve(parent.ParentDir, path);
            var module = await AddToBundle(bundle, file, width, height, format, inlineOverride);

            if (bundle is null)
            {
                parent.References.Add(module);
            }
            else
            {
                parent.Children.Add(module);
            }

            return module;
        }
        catch (Exception err)
        {
            // Nothing resolved locally. A bare specifier that is a known runtime
            // built-in (e.g. `fs` / `path` / `test` on Node) is provided by the
            // runtime — keep it external, canonicalized to the `node:` scheme.
            var builtin = _context.Platform.BuiltinFallback(name);
            if (builtin is not null)
            {
                return AddExternalReference(parent, builtin);
            }

            if (!_quiet)
            {
                Console.Error.WriteLine("[netpack] error: failed to process '{0}': {1}", parent.FileName, err.Message);
            }

            return null;
        }
    }

    /// <summary>Image variant output formats accepted in a `?format=` query
    /// param — the same raster formats the image asset processor can reliably
    /// encode to. An unrecognized value is ignored (treated as if no format were
    /// requested) rather than failing the build.</summary>
    private static readonly HashSet<string> SupportedVariantFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "webp", "gif", "bmp",
    };

    /// <summary>
    /// Splits a trailing `?...` query string off a reference/import specifier
    /// and picks out `width`/`height`/`format` params for an on-the-fly image
    /// variant, and `inline` for a per-import inlining override. Any other
    /// query params are accepted and silently ignored (resolution only ever
    /// needs the part before the `?`).
    /// </summary>
    private static (string Path, (int? Width, int? Height, string? Format) Variant, int? InlineOverride) ParseVariantQuery(string name)
    {
        var index = name.IndexOf('?');

        if (index < 0)
        {
            return (name, default, null);
        }

        var path = name[..index];
        var query = name[(index + 1)..];
        int? width = null;
        int? height = null;
        string? format = null;
        int? inlineOverride = null;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = parts[0];
            var value = parts.Length > 1 ? parts[1] : "";

            if (key == "width" && int.TryParse(value, out var w) && w > 0)
            {
                width = w;
            }
            else if (key == "height" && int.TryParse(value, out var h) && h > 0)
            {
                height = h;
            }
            else if (key == "format" && SupportedVariantFormats.Contains(value))
            {
                format = value.ToLowerInvariant();
            }
            else if (key == "inline")
            {
                inlineOverride = ParseInlineOverride(value);
            }
        }

        return (path, (width, height, format), inlineOverride);
    }

    /// <summary>
    /// Parses the value of a <c>?inline=</c> query parameter:
    /// <c>always</c> → 0 (force inline),
    /// <c>never</c> → -1 (block inline),
    /// a positive integer → threshold override in KB (converted to bytes).
    /// Anything else returns null (no override).
    /// </summary>
    private static int? ParseInlineOverride(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Equals("always", StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        if (value.Equals("never", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        if (int.TryParse(value, out var kb) && kb > 0)
        {
            return kb * 1024;
        }

        return null;
    }

    private async Task ProcessAsset(Node current, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var hash = await Hash.ComputeHash(stream);

        if (current.IsVariant)
        {
            // ComputeHash hashes the on-disk (pre-resize/pre-reencode) bytes,
            // which are identical for the original and every one of its
            // variants — fold the requested dimensions/format in so each
            // variant still gets its own hash, and therefore its own output
            // filename.
            hash = Hash.Short($"{hash}-w{current.VariantWidth}-h{current.VariantHeight}-f{current.VariantFormat}");
        }

        _context.Assets.TryAdd(current, new Asset(current, current.Type, bytes, hash));
    }

    private async Task ProcessCodegen(Node current, byte[] bytes, Bundle bundle)
    {
        var content = await TranspileCodegen(current.FileName);
        var fragment = await ParseJsModule(bundle, current, content);
        _context.JsFragments.TryAdd(current, fragment);
    }

    private async Task ProcessJson(Node current, byte[] bytes, Bundle bundle)
    {
        if (bundle is JsBundle)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            var newContent = $"export default ({content})";
            var ast = Parser.ParseModule(newContent, current.FileName, ParserOptions.ForFile(current.FileName));
            var visitor = new JsVisitor(bundle, current, InnerProcess);
            var fragment = await visitor.FindChildren(ast);
            _context.JsFragments.TryAdd(current, fragment);
        }
        else
        {
            await ProcessAsset(current, bytes);
        }
    }

    private async Task ProcessStyleSheet(Node current, byte[] bytes, Bundle bundle)
    {
        var enableSass = _context.Features.HasFlag(FeatureFlags.Sass);
        var enableLess = _context.Features.HasFlag(FeatureFlags.Less);
        var enablePostCss = _context.Features.HasFlag(FeatureFlags.PostCss);

        if (enableSass && (current.FileName.EndsWith(".scss") || current.FileName.EndsWith(".sass")))
        {
            using var istream = new MemoryStream(bytes);
            using var reader = new StreamReader(istream);
            var content = await reader.ReadToEndAsync();
            content = await TranspileSass(content, current.FileName);
            bytes = Encoding.UTF8.GetBytes(content);
        }

        if (enableLess && current.FileName.EndsWith(".less"))
        {
            using var istream = new MemoryStream(bytes);
            using var reader = new StreamReader(istream);
            var content = await reader.ReadToEndAsync();
            content = await TranspileLess(content, current.FileName);
            bytes = Encoding.UTF8.GetBytes(content);
        }

        if (enablePostCss)
        {
            using var istream = new MemoryStream(bytes);
            using var reader = new StreamReader(istream);
            var content = await reader.ReadToEndAsync();
            content = await TranspilePostCss(content, current.FileName);
            bytes = Encoding.UTF8.GetBytes(content);
        }

        using var stream = new MemoryStream(bytes);
        var tasks = new List<Task<Node?>>();
        var options = new CssParserOptions
        {
            IsIncludingUnknownRules = true,
            IsIncludingUnknownDeclarations = true,
            IsToleratingInvalidSelectors = true,
        };
        var parser = new CssParser(options, _browser);
        var sheet = await parser.ParseStyleSheetAsync(stream);
        var visitor = new CssVisitor(bundle, current, InnerProcess);
        var fragment = await visitor.FindChildren(sheet);

        // Compute content hash for CSS fragments so render cache can detect changes.
        if (_context.Snapshot is not null || _context.RenderCache is not null)
        {
            stream.Position = 0;
            var cssHash = await Hash.ComputeHash(stream);
            fragment.ContentHash = cssHash;
            current.ContentHash = cssHash;
        }

        _context.CssFragments.TryAdd(current, fragment);
    }

    /// <summary>
    /// Compiles an Astro single-file component (.astro) into a virtual JavaScript
    /// module. Unlike Vue's SFC (which is split via AngleSharp/HTML parsing),
    /// <see cref="AstroSfc"/> parses the template as JSX directly — see its own
    /// doc comment for why (case-sensitive component-vs-host-element detection).
    /// The compiled module goes through the same <see cref="ParseJsModule"/> path
    /// as any other JS module afterwards, so its imports (e.g. another `.astro`
    /// file used as a component) are resolved exactly like any other dependency.
    /// </summary>
    private async Task ProcessAstro(Node current, byte[] bytes, Bundle bundle)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var source = AstroSfc.Generate(text, current.FileName);
        var fragment = await ParseJsModule(bundle, current, source);
        _context.JsFragments.TryAdd(current, fragment);
    }

    /// <summary>
    /// Compiles a Svelte component (.svelte) by handing it to the Svelte compiler
    /// over the Node bridge (<see cref="NodeJs"/>) — the same IPC used for Sass /
    /// LESS / PostCSS. The compiler emits an ES module that imports Svelte's runtime
    /// (bundled normally) and injects the component's styles at runtime, so the
    /// result is parsed like any other JavaScript module. Requires <c>svelte</c> to
    /// be installed in the project.
    /// </summary>
    private async Task ProcessSvelte(Node current, byte[] bytes, Bundle bundle)
    {
        var content = Encoding.UTF8.GetString(bytes);
        var response = await _njs.RunCommand("svelte", content, current.FileName);
        var result = response.Deserialize(SourceGenerationContext.Default.SvelteCommandResult);
        var source = result?.Js ?? "";
        var fragment = await ParseJsModule(bundle, current, source);
        _context.JsFragments.TryAdd(current, fragment);
    }

    /// <summary>
    /// Compiles a Vue single-file component (.vue) into a virtual JavaScript module.
    /// AngleSharp splits the file into its top-level &lt;template&gt;, &lt;script&gt;
    /// and &lt;style&gt; blocks; <see cref="VueSfc"/> then assembles a module that
    /// exports the component (with the template attached as a string for Vue's
    /// runtime compiler, scoped styles applied, and CSS injected at runtime).
    /// Blocks carrying a <c>src</c> attribute are loaded from the referenced file.
    /// </summary>
    private async Task ProcessVue(Node current, byte[] bytes, Bundle bundle)
    {
        using var stream = new MemoryStream(bytes);
        var document = await _browser.OpenAsync(res => res.Content(stream));

        // querySelectorAll does not descend into <template> content (it lives in a
        // separate fragment), so tags nested in the template are not seen as blocks.
        var templateEl = document.QuerySelector("template");
        var scriptEls = document.QuerySelectorAll("script").ToList();
        var styleEls = document.QuerySelectorAll("style").ToList();

        var setupEl = scriptEls.FirstOrDefault(s => s.HasAttribute("setup"));
        var classicEl = scriptEls.FirstOrDefault(s => !s.HasAttribute("setup"));

        var relative = Path.GetRelativePath(_context.Root, current.FileName).Replace('\\', '/');
        var scopeId = $"data-v-{Hash.Short(relative)}";

        var script = await ReadVueBlock(current, classicEl, isTemplate: false);
        var scriptSetup = await ReadVueBlock(current, setupEl, isTemplate: false);

        var styles = new List<VueStyleBlock>();

        foreach (var styleEl in styleEls)
        {
            var css = await ReadVueBlock(current, styleEl, isTemplate: false) ?? "";
            css = await PreprocessVueStyle(css, current.FileName, styleEl.GetAttribute("lang"));
            var scoped = styleEl.HasAttribute("scoped");

            if (scoped && css.Length > 0)
            {
                css = await ScopeVueStyle(css, $"[{scopeId}]");
            }

            styles.Add(new VueStyleBlock { Css = css, Scoped = scoped });
        }

        // Prefer build-time precompilation; fall back to the raw template string
        // (Vue's runtime compiler) for any construct outside the supported subset.
        var templateInfo = await ReadTemplateNodes(current, templateEl);
        string? templateMarkup = null;
        string? renderBody = null;
        IReadOnlyCollection<string> renderHelpers = [];
        IReadOnlyCollection<string> renderComponents = [];

        if (templateInfo is { } info)
        {
            try
            {
                var render = VueTemplateCompiler.Compile(info.Nodes);
                renderBody = render.Body;
                renderHelpers = render.Helpers;
                renderComponents = render.Components;
            }
            catch (VueTemplateException)
            {
                templateMarkup = info.Markup;
            }
        }

        var descriptor = new VueDescriptor
        {
            Template = templateMarkup,
            RenderBody = renderBody,
            RenderHelpers = renderHelpers,
            RenderComponents = renderComponents,
            Script = script,
            ScriptSetup = scriptSetup,
            Styles = styles,
            RelativePath = relative,
            ScopeId = scopeId,
        };

        var source = VueSfc.Generate(descriptor);
        var fragment = await ParseJsModule(bundle, current, source);
        _context.JsFragments.TryAdd(current, fragment);
    }

    /// <summary>
    /// Returns the top-level DOM nodes of the <c>&lt;template&gt;</c> block (for
    /// build-time compilation) together with its serialized markup (for the runtime
    /// fallback). Honors a <c>src</c> attribute. Null when there is no template.
    /// </summary>
    private async Task<(IReadOnlyList<AngleSharp.Dom.INode> Nodes, string Markup)?> ReadTemplateNodes(
        Node current, AngleSharp.Dom.IElement? templateEl)
    {
        if (templateEl is null)
        {
            return null;
        }

        var src = templateEl.GetAttribute("src");

        if (!string.IsNullOrEmpty(src))
        {
            var path = await Resolve(current.ParentDir, src);
            var text = await File.ReadAllTextAsync(path);
            using var s = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var doc = await _browser.OpenAsync(res => res.Content(s));
            var body = doc.Body;
            var nodes = body?.ChildNodes.ToList() ?? new List<AngleSharp.Dom.INode>();
            return (nodes, (body?.InnerHtml ?? text).Trim());
        }

        if (templateEl is AngleSharp.Html.Dom.IHtmlTemplateElement tpl)
        {
            return (tpl.Content.ChildNodes.ToList(), tpl.Content.ToHtml().Trim());
        }

        return (templateEl.ChildNodes.ToList(), templateEl.InnerHtml.Trim());
    }

    /// <summary>
    /// Returns the text of a single SFC block. A block with a <c>src</c> attribute
    /// is read from the referenced file (resolved relative to the .vue file);
    /// otherwise the inline content is used. Template content is serialized from the
    /// element's template fragment.
    /// </summary>
    private async Task<string?> ReadVueBlock(Node current, AngleSharp.Dom.IElement? element, bool isTemplate)
    {
        if (element is null)
        {
            return null;
        }

        var src = element.GetAttribute("src");

        if (!string.IsNullOrEmpty(src))
        {
            var path = await Resolve(current.ParentDir, src);
            var text = await File.ReadAllTextAsync(path);

            if (isTemplate)
            {
                using var s = new MemoryStream(Encoding.UTF8.GetBytes(text));
                var doc = await _browser.OpenAsync(res => res.Content(s));
                return (doc.Body?.InnerHtml ?? text).Trim();
            }

            return text.Trim();
        }

        if (isTemplate)
        {
            // The template element keeps its markup in a separate content fragment.
            var markup = element is AngleSharp.Html.Dom.IHtmlTemplateElement tpl
                ? tpl.Content.ToHtml()
                : element.InnerHtml;
            return markup.Trim();
        }

        return element.TextContent.Trim();
    }

    private async Task<string> PreprocessVueStyle(string css, string file, string? lang)
    {
        if (css.Length == 0)
        {
            return css;
        }

        if ((lang == "scss" || lang == "sass") && _context.Features.HasFlag(FeatureFlags.Sass))
        {
            return await TranspileSass(css, file);
        }

        if (lang == "less" && _context.Features.HasFlag(FeatureFlags.Less))
        {
            return await TranspileLess(css, file);
        }

        return css;
    }

    private async Task<string> ScopeVueStyle(string css, string scopeAttribute)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(css));
        var options = new CssParserOptions
        {
            IsIncludingUnknownRules = true,
            IsIncludingUnknownDeclarations = true,
            IsToleratingInvalidSelectors = true,
        };
        var parser = new CssParser(options, _browser);
        var sheet = await parser.ParseStyleSheetAsync(stream);
        return CssModules.ApplyScope(sheet, scopeAttribute);
    }

    private async Task<JsFragment> ParseJsModule(Bundle bundle, Node current, string content)
    {
        var options = ParserOptions.ForFile(current.FileName);
        var ast = Parser.ParseModule(content, current.FileName, options);

        // Store parsed AST in cache for next rebuild.
        if (_buildCache is not null)
        {
            var contentHash = await ComputeContentHash(current.FileName, content, _context);
            _buildCache.Put(current.FileName, contentHash, ast);
        }

        var fragment = await ParseJsModuleFromAst(bundle, current, ast);
        ApplyJsxFactory(current, content, options.TypeScript, fragment);
        return fragment;
    }

    private async Task<JsFragment> ParseJsModuleFromAst(Bundle bundle, Node current, Syntax.Ast.SourceFile ast)
    {
        var visitor = new JsVisitor(bundle, current, InnerProcess);
        var fragment = await visitor.FindChildren(ast);
        RegisterCssImports(bundle, fragment);
        return fragment;
    }

    /// <summary>
    /// Records CSS files this module imports so they can later be turned into
    /// virtual JS modules. An import that carries named/default bindings marks the
    /// CSS file as a CSS module (class names are hashed).
    ///
    /// CSS files are tracked in source-declaration order (matching the JS module's
    /// AST body) rather than resolution-completion order, so the per-bundle CSS
    /// lists reflect the actual evaluation sequence.
    /// </summary>
    private void RegisterCssImports(Bundle bundle, JsFragment fragment)
    {
        // Walk the AST body in source order to find import declarations,
        // then look up each one in the replacements map to get its resolved node.
        // This preserves declaration order regardless of which dependency resolved first.
        foreach (var stmt in fragment.Ast.Body)
        {
            if (stmt is Syntax.Ast.ImportDeclaration import
                && fragment.Replacements.TryGetValue(import, out var graphNode)
                && graphNode.Type == ".css")
            {
                _context.CssImports.TryAdd(graphNode, bundle);

                // Record the post-order index of the CSS module so CSS files
                // are ordered by their position in the evaluation chain.
                _context.CssImporterOrder.TryAdd(graphNode, graphNode.PostOrderIndex);

                // Record per-bundle CSS import order for conflict detection
                _context.CssPerBundleOrder.AddOrUpdate(
                    bundle,
                    _ => new List<Node> { graphNode },
                    (_, list) =>
                    {
                        lock (list) { list.Add(graphNode); }
                        return list;
                    });

                // Track all bundles that import this CSS file for code splitting
                _context.CssImportedByBundles.AddOrUpdate(
                    graphNode,
                    _ => new HashSet<Bundle> { bundle },
                    (_, existing) =>
                    {
                        lock (existing)
                        {
                            existing.Add(bundle);
                        }
                        return existing;
                    });

                if (import.Specifiers.Count > 0)
                {
                    _context.CssModuleNodes[graphNode] = true;
                }
            }
        }
    }

    /// <summary>
    /// Converts every CSS file imported from JavaScript into a virtual JS module:
    /// class selectors are hashed (for CSS modules), the CSS is set up for runtime
    /// injection, and the original→hashed class map is exported. Runs after the
    /// graph is built but before bundles are assembled.
    /// 
    /// Shared CSS (imported by multiple entry points) is kept as separate CSS
    /// bundles and not transformed into virtual JS modules.
    /// </summary>
    private async Task TransformCssModules(IDictionary<Node, string>? sharedCss = null)
    {
        // Process CSS modules in post-order so their virtual JS modules are
        // registered in the same order as the JS modules that imported them.
        var sortedCssImports = _context.CssImports.ToArray()
            .OrderBy(kv => _context.CssImporterOrder.TryGetValue(kv.Key, out var order) ? order : 0);

        foreach (var (node, bundle) in sortedCssImports)
        {
            // Skip shared CSS - it's already been created as a separate CSS bundle
            if (sharedCss is not null && sharedCss.ContainsKey(node))
            {
                continue;
            }

            if (!_context.CssFragments.TryRemove(node, out var cssFragment))
            {
                continue;
            }

            var relative = Path.GetRelativePath(_context.Root, node.FileName).Replace('\\', '/');
            var isModule = _context.CssModuleNodes.ContainsKey(node);
            var (map, css) = CssModules.Rewrite(cssFragment.Stylesheet, relative, isModule);
            var source = CssModules.GenerateModule(css, map);
            var fragment = await ParseJsModule(bundle, node, source);
            _context.JsFragments.TryAdd(node, fragment);
        }
    }

    /// <summary>
    /// Detects ordering conflicts among CSS modules shared across multiple
    /// chunk groups and emits a warning for each unresolved pair.
    /// </summary>
    private void DetectCssOrderConflicts()
    {
        var conflicts = CssOrdering.DetectConflicts(_context);

        foreach (var conflict in conflicts)
        {
            var nameA = Path.GetFileName(conflict.ModuleA.FileName);
            var nameB = Path.GetFileName(conflict.ModuleB.FileName);

            Console.Error.WriteLine(
                "[netpack] warning: Conflicting CSS order between {0} and {1}. " +
                "These modules appear in different orders across chunk groups and the " +
                "output cascade may differ from source order.",
                nameA, nameB);
        }
    }

    /// <summary>
    /// Writes the computed CSS module order to stderr for diagnostic purposes.
    /// Activated when <c>NETPACK_DEBUG_CSS_ORDER=1</c>.
    /// </summary>
    public static void DebugCssOrder(BundlerContext context)
    {
        var ordered = CssOrdering.GetOrderedCssFiles(context);
        Console.Error.WriteLine("[netpack] CSS module order (by JS evaluation):");
        for (var i = 0; i < ordered.Count; i++)
        {
            Console.Error.WriteLine("  {0}: {1}", i + 1, ordered[i]);
        }
    }

    /// <summary>
    /// Resolves the JSX factory (and fragment factory) for a single module. A
    /// local <c>@jsx</c> / <c>@jsxFrag</c> pragma wins over the project-wide
    /// <c>tsconfig.json</c> setting (which only applies to TypeScript files),
    /// which in turn wins over the <c>React.createElement</c> default baked into
    /// <see cref="JsFragment"/>.
    /// </summary>
    private void ApplyJsxFactory(Node current, string content, bool isTypeScript, JsFragment fragment)
    {
        var pragma = JsxPragma.Scan(content);

        var tsFactory = isTypeScript ? _context.JsxFactory : null;
        var tsFragmentFactory = isTypeScript ? _context.JsxFragmentFactory : null;

        var isUsingDefaultRuntime = pragma.Factory is null
            && pragma.FragmentFactory is null
            && string.IsNullOrEmpty(tsFactory)
            && string.IsNullOrEmpty(tsFragmentFactory)
            && !string.IsNullOrEmpty(_context.DefaultJsxFactory)
            && !string.IsNullOrEmpty(_context.DefaultJsxImportModule)
            && !string.IsNullOrEmpty(_context.DefaultJsxImportIdentifier);

        var factory = pragma.Factory ?? tsFactory ?? _context.DefaultJsxFactory;
        if (!string.IsNullOrEmpty(factory))
        {
            fragment.JsxFactory = factory;
        }

        var fragmentFactory = pragma.FragmentFactory ?? tsFragmentFactory ?? _context.DefaultJsxFragmentFactory;
        if (!string.IsNullOrEmpty(fragmentFactory))
        {
            fragment.JsxFragmentFactory = fragmentFactory;
        }

        if (isUsingDefaultRuntime)
        {
            fragment.AutoJsxImportModule = _context.DefaultJsxImportModule;
            fragment.AutoJsxImportIdentifier = _context.DefaultJsxImportIdentifier;
        }
    }

    private async Task ProcessJavaScript(Node current, byte[] bytes, Bundle bundle)
    {
        await FireModuleAsync(_context.Hooks?.Compilation.BuildModule, current);

        try
        {
            await ProcessJavaScriptCore(current, bytes, bundle);
            await FireModuleAsync(_context.Hooks?.Compilation.SucceedModule, current);
        }
        catch (Exception error)
        {
            var hook = _context.Hooks?.Compilation.FailedModule;

            if (hook is { Count: > 0 })
            {
                await hook.CallAsync(new NetPack.Plugins.ModuleBuildContext
                {
                    BundlerContext = _context,
                    Module = current,
                    IsDevelopment = _devServer,
                    Error = error,
                });
            }

            throw;
        }
    }

    private async Task ProcessJavaScriptCore(Node current, byte[] bytes, Bundle bundle)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // TypeScript is stripped natively by the parser (see ParserOptions.ForFile),
        // so .ts/.tsx no longer need an external `tsc` pass. The remaining source
        // transform is compile-time constant substitution (--define), which
        // includes the built-in process.env.NODE_ENV default.
        var newContent = content;

        foreach (var (key, replacement) in _context.Defines)
        {
            newContent = newContent.Replace(key, replacement);
        }

        // Replace import.meta.env.X references with their values from .env files
        if (_context.EnvVars.Count > 0)
        {
            foreach (var (key, value) in _context.EnvVars)
            {
                newContent = newContent.Replace($"import.meta.env.{key}", value);
            }
        }

        // For a Solid project, JSX files are compiled by Solid's official transform
        // (babel-preset-solid) into fine-grained DOM/reactivity code before parsing —
        // Solid's JSX is not a `createElement`-style factory call, so netpack's own
        // JSX lowering must not run on it.
        if (_context.UseSolid && IsJsxFile(current))
        {
            newContent = await CompileSolid(newContent, current.FileName);
        }

        // Compute content hash for parse and codegen caches.
        // Solid-compiled files bypass both (their output is opaque).
        var wantHash = (_buildCache is not null || _context.CodegenCache is not null) && !_context.UseSolid;
        var contentHash = wantHash
            ? await ComputeContentHash(current.FileName, newContent, _context)
            : null;

        JsFragment? cachedFragment = null;

        if (_buildCache is not null && contentHash is not null)
        {
            if (_buildCache.Get(current.FileName, contentHash)?.Fragment is Syntax.Ast.SourceFile cachedAst)
            {
                cachedFragment = await ParseJsModuleFromAst(bundle, current, cachedAst);
                var options = ParserOptions.ForFile(current.FileName);
                ApplyJsxFactory(current, content, options.TypeScript, cachedFragment);

                // Reused unchanged from the previous build (watch/incremental).
                await FireModuleAsync(_context.Hooks?.Compilation.StillValidModule, current);
            }
        }

        var fragment = cachedFragment ?? await ParseJsModule(bundle, current, newContent);
        fragment.ContentHash = contentHash;
        current.ContentHash = contentHash;
        _context.JsFragments.TryAdd(current, fragment);

        // Record raw file hash in the build snapshot for cross-build comparison.
        if (_context.Snapshot is not null)
        {
            var absPath = Path.GetFullPath(Path.Combine(_context.Root, current.FileName));
            using var fs = File.OpenRead(absPath);
            var fileHash = await Hash.ComputeHash(fs);
            _context.Snapshot.Record(absPath, fileHash);
        }
    }

    private static async Task<string> ComputeContentHash(string filePath, string content, BundlerContext context)
    {
        // The content already includes define/env substitutions, which are the only
        // build options that affect parsing. Platform, format, loaders, and conditions
        // affect graph resolution, not the AST — so they are not part of the key.
        var key = $"{filePath}:{content}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(key));
        return await Hash.ComputeHash(stream);
    }

    /// <summary>True for a JSX-bearing source file (<c>.jsx</c>/<c>.tsx</c>).</summary>
    private static bool IsJsxFile(Node current)
    {
        var ext = current.Extension.ToLowerInvariant();
        return ext is ".jsx" or ".tsx";
    }

    /// <summary>
    /// Compiles a Solid JSX/TSX source with <c>babel-preset-solid</c> over the Node
    /// bridge (<see cref="NodeJs"/>) — the same IPC used for Sass/Svelte. The result
    /// is plain JavaScript (Solid's dom-expressions output, importing its runtime
    /// from <c>solid-js/web</c>), parsed like any other module. Requires
    /// <c>@babel/core</c> and <c>babel-preset-solid</c> to be installed.
    /// </summary>
    private async Task<string> CompileSolid(string content, string file)
    {
        var response = await _njs.RunCommand("solid", content, file);
        var result = response.Deserialize(SourceGenerationContext.Default.SolidCommandResult);
        return result?.Js ?? "";
    }

    /// <summary>The <c>--loader</c> override for a node's extension, or null when
    /// the built-in handling applies.</summary>
    private string? ResolveLoader(Node current)
        => _context.Loaders.TryGetValue(current.Extension.ToLowerInvariant(), out var loader) ? loader : null;

    /// <summary>
    /// Processes a file according to an explicit <c>--loader</c>, overriding the
    /// extension-based default. JS-producing loaders (text/base64/dataurl/empty)
    /// only apply inside a JS bundle; elsewhere they fall back to emitting a file.
    /// </summary>
    private async Task ProcessWithLoader(string loader, Node current, byte[] bytes, Bundle bundle)
    {
        switch (loader)
        {
            case "js" or "jsx" or "ts" or "tsx":
                await ProcessJavaScript(current, bytes, bundle);
                break;
            case "json":
                await ProcessJson(current, bytes, bundle);
                break;
            case "css":
                await ProcessStyleSheet(current, bytes, bundle);
                break;
            case "text":
                await ProcessInlineModule(current, bytes, JsonString(Encoding.UTF8.GetString(bytes)), bundle);
                break;
            case "base64":
                await ProcessInlineModule(current, bytes, JsonString(Convert.ToBase64String(bytes)), bundle);
                break;
            case "dataurl":
                var dataUrl = ToDataUri(current.Extension, bytes);
                await ProcessInlineModule(current, bytes, JsonString(dataUrl), bundle);
                break;
            case "empty":
                await ProcessInlineModule(current, bytes, "{}", bundle);
                break;
            case "file" or "copy":
                await ProcessAsset(current, bytes);
                break;
            default:
                throw new InvalidOperationException($"Unknown loader '{loader}'. Available: js, jsx, ts, tsx, json, css, text, base64, dataurl, file, copy, empty.");
        }
    }

    /// <summary>
    /// Emits a synthetic JS module whose default export is
    /// <paramref name="expression"/> (already valid JS source). Used by the
    /// text/base64/dataurl/empty loaders. Falls back to a plain asset when the
    /// importer isn't a JS bundle.
    /// </summary>
    private async Task ProcessInlineModule(Node current, byte[] bytes, string expression, Bundle bundle)
    {
        if (bundle is not JsBundle)
        {
            await ProcessAsset(current, bytes);
            return;
        }

        var newContent = $"export default ({expression})";
        var ast = Parser.ParseModule(newContent, current.FileName, ParserOptions.ForFile(current.FileName));
        var visitor = new JsVisitor(bundle, current, InnerProcess);
        var fragment = await visitor.FindChildren(ast);
        _context.JsFragments.TryAdd(current, fragment);
    }

    // Uses the source-generated type info (not the reflection-based overload) so
    // the AoT build stays trim/native-AoT safe.
    private static string JsonString(string value) => JsonSerializer.Serialize(value, SourceGenerationContext.Default.String);

    private async Task ProcessHtml(Node current, byte[] bytes, Bundle bundle)
    {
        using var stream = new MemoryStream(bytes);
        var tasks = new List<Task<Node?>>();
        var document = await _browser.OpenAsync(res => res.Content(stream));
        var elements = new List<AngleSharp.Dom.IElement>();
        await AddStaticAssets(current, Path.Combine(current.ParentDir, "public"));
        var visitor = new HtmlVisitor(bundle, current, InnerProcess, AddExternal);
        var fragment = await visitor.FindChildren(document);
        _context.HtmlFragments.TryAdd(current, fragment);
    }

    private async Task AddModuleFederationDependency(string name)
    {
        var path = await ResolveFromNodeModules(_context.Root, name);

        if (!string.IsNullOrEmpty(path))
        {
            _context.Aliases.TryAdd(name, "..."); //TODO virtual module here
            _context.Aliases.TryAdd($"shared:{name}", path);
        }
    }

    /// <summary>
    /// Reads a <c>federation.json</c> entry and dispatches on its <c>kind</c>:
    /// <c>module</c> (default) builds a Module Federation container; <c>native</c>
    /// builds a plain ESM native-federation remote.
    /// </summary>
    private async Task<Node> AddFederation(string entry)
    {
        var definition = await ModuleFederationHelpers.ReadFrom(entry);
        var kind = ModuleFederationHelpers.NormalizeKind(definition.Kind);

        return kind == "native"
            ? await AddNativeFederation(definition, entry)
            : await AddModuleFederation(definition, entry);
    }

    private async Task<Node> AddModuleFederation(ModuleFederation definition, string entry)
    {
        if (definition.Shared is not null && definition.Shared.Count > 0)
        {
            await Task.WhenAll(definition.Shared.Keys.Select(AddModuleFederationDependency));
        }

        var code = await ModuleFederationHelpers.CreateContainerCode(_context, definition);
        var fileName = Path.Combine(Path.GetDirectoryName(entry)!, definition.FileName);
        var node = new Node(fileName, code.Length);
        var bundle = CreateBundle(node, BundleFlags.Primary);
        _context.Modules.TryAdd(fileName, node);
        _context.Bundles.TryAdd(node, bundle);
        var fragment = await ParseJsModule(bundle, node, code);
        _context.JsFragments.TryAdd(node, fragment);
        return node;
    }

    /// <summary>
    /// Builds a native-federation remote. Shared dependencies are treated as
    /// externals (so every <c>import … from "&lt;dep&gt;"</c> stays a bare ESM
    /// import) and are additionally emitted as their own standalone ESM bundles;
    /// the generated remote entry is a plain ES module.
    /// </summary>
    private async Task<Node> AddNativeFederation(ModuleFederation definition, string entry)
    {
        var sharedNames = definition.Shared?.Keys.ToList() ?? [];

        foreach (var name in sharedNames)
        {
            AddExternal(name);

            if (!_context.Shared.Contains(name))
            {
                _context.Shared.Add(name);
            }
        }

        var code = ModuleFederationHelpers.CreateNativeContainerCode(definition);
        var fileName = Path.Combine(Path.GetDirectoryName(entry)!, definition.FileName);
        var node = new Node(fileName, code.Length);
        var bundle = CreateBundle(node, BundleFlags.Primary);
        _context.Modules.TryAdd(fileName, node);
        _context.Bundles.TryAdd(node, bundle);
        var fragment = await ParseJsModule(bundle, node, code);
        _context.JsFragments.TryAdd(node, fragment);

        // Emit each shared dependency as its own ESM file (host wires it up via an
        // import map). Bundling by resolved entry path gives it the dependency's
        // name (e.g. react.js) and keeps it separate from the bare "react" import.
        foreach (var name in sharedNames)
        {
            var path = await ResolveFromNodeModules(_context.Root, name);

            if (!string.IsNullOrEmpty(path))
            {
                await AddNewBundle(path);
            }
        }

        return node;
    }

    private void AddExternal(string name)
    {
        if (!_context.Externals.Contains(name))
        {
            _context.Externals.Add(name);
        }
    }

    private async Task AddStaticAssets(Node current, string publicDir)
    {
        if (Directory.Exists(publicDir))
        {
            var files = Directory.GetFiles(publicDir, "*", SearchOption.AllDirectories);
            await Task.WhenAll(files.Select(file => AddStaticAsset(current, file)));
        }
    }

    private async Task<Node> AddStaticAsset(Node parent, string fileName)
    {
        if (!_context.Modules.TryGetValue(fileName, out var node))
        {
            var bytes = await File.ReadAllBytesAsync(fileName);
            node = new Node(fileName, bytes.Length);
            _context.Assets.TryAdd(node, new Asset(node, node.Type, bytes));
            _context.Modules.TryAdd(fileName, node);
        }

        parent.Children.Add(node);
        return node;
    }

    private Node AddExternalReference(Node parent, string name)
    {
        if (!_context.Modules.TryGetValue(name, out var node))
        {
            node = new Node(name, 0);
            _context.Modules.TryAdd(name, node);
            _context.JsFragments.TryAdd(node, JsExternalFragment.CreateFrom(node));
        }

        parent.Children.Add(node);
        return node;
    }

    private Task<Node> AddNewBundle(string fileName) => AddToBundle(null, fileName);

    /// <summary>
    /// The <see cref="BundlerContext.Modules"/> / in-flight <see cref="_reserved"/>
    /// key for a reference. A plain reference keys on its file path, same as
    /// always; a variant request (distinct width/height) gets a distinct key so
    /// it becomes its own <see cref="Node"/> — and, later, its own resized
    /// <see cref="Asset"/> — instead of collapsing onto the original file's node.
    /// </summary>
    private static string GetModuleKey(string fileName, int? variantWidth, int? variantHeight, string? variantFormat, int? inlineLimitOverride)
        => variantWidth is null && variantHeight is null && variantFormat is null && inlineLimitOverride is null
            ? fileName
            : $"{fileName}?w={variantWidth}&h={variantHeight}&f={variantFormat}&inline={inlineLimitOverride}";

    private async Task<Node> AddToBundle(Bundle? bundle, string fileName, int? variantWidth = null, int? variantHeight = null, string? variantFormat = null, int? inlineLimitOverride = null)
    {
        var key = GetModuleKey(fileName, variantWidth, variantHeight, variantFormat, inlineLimitOverride);

        if (!_context.Modules.TryGetValue(key, out var node))
        {
            // GetOrAdd may build the Lazy wrapper more than once, but only one is
            // stored and only its .Value ever runs — so AddNewNodeToBundle executes
            // exactly once for this key, and every concurrent importer awaits the
            // same task and receives the same node.
            var reserved = _reserved.GetOrAdd(key, _ => new Lazy<Task<Node>>(
                () => AddNewNodeToBundle(bundle, fileName, variantWidth, variantHeight, variantFormat, inlineLimitOverride)));
            node = await reserved.Value;
            _reserved.TryRemove(key, out _);
        }

        return node;
    }

    private async Task<Node> AddNewNodeToBundle(Bundle? bundle, string fileName, int? variantWidth = null, int? variantHeight = null, string? variantFormat = null, int? inlineLimitOverride = null)
    {
        var bytes = await File.ReadAllBytesAsync(fileName);
        var node = new Node(fileName, bytes.Length, variantWidth, variantHeight, variantFormat, inlineLimitOverride);
        _context.Modules.TryAdd(GetModuleKey(fileName, variantWidth, variantHeight, variantFormat, inlineLimitOverride), node);

        if (bundle is null)
        {
            var flags = _context.Bundles.IsEmpty ? BundleFlags.Primary : BundleFlags.None;

            if (TryCreateBundle(node, flags, out var newBundle))
            {
                _context.Bundles.TryAdd(node, newBundle);
                bundle = newBundle;
            }
            else
            {
                await ProcessAsset(node, bytes);
                return node;
            }
        }

        // Vue SFCs compile to JS (their extension maps to ".js"), so dispatch on the
        // raw extension before the type switch to route them through ProcessVue.
        if (node.Extension == ".vue")
        {
            await ProcessVue(node, bytes, bundle);
            return node;
        }

        // Same idea for Astro SFCs: ".astro" also maps to ".js" in ExtensionMap.
        if (node.Extension == ".astro")
        {
            await ProcessAstro(node, bytes, bundle);
            return node;
        }

        // Svelte components are compiled by the Svelte compiler over the Node bridge.
        if (node.Extension == ".svelte")
        {
            await ProcessSvelte(node, bytes, bundle);
            return node;
        }

        // An explicit --loader for this extension overrides the built-in handling.
        var loader = ResolveLoader(node);

        if (loader is not null)
        {
            await ProcessWithLoader(loader, node, bytes, bundle);
            return node;
        }

        await (node.Type switch
        {
            ".js" => ProcessJavaScript(node, bytes, bundle),
            ".html" => ProcessHtml(node, bytes, bundle),
            ".css" => ProcessStyleSheet(node, bytes, bundle),
            ".json" => ProcessJson(node, bytes, bundle),
            ".codegen" => ProcessCodegen(node, bytes, bundle),
            _ => ProcessAsset(node, bytes),
        });

        return node;
    }

    /// <summary>
    /// Builds a metafile JSON container (esbuild-compatible) from the graph context
    /// and the list of emitted files. The manifest contains inputs (source modules
    /// with their dependencies), outputs (bundles/assets with byte sizes, entry-point
    /// flags, and cross-bundle references), and per-chunk and per-asset metadata.
    /// </summary>
    public static string BuildMetafile(BundlerContext context, IReadOnlyList<EmittedFile> emitted, NetPack.Json.AuditReport? audit = null)
    {
        var root = Environment.CurrentDirectory;
        var container = new MetadataContainer
        {
            Inputs = [],
            Outputs = [],
            Audit = audit,
        };

        // Build a map from node to output file name for cross-references
        var nodeToOutput = new Dictionary<Node, string>();
        foreach (var bundle in context.Bundles.Values)
        {
            nodeToOutput[bundle.Root] = bundle.GetFileName();
        }

        // Build inputs section: every JS module with its imports
        foreach (var module in context.Modules.Values)
        {
            if (module.Type == ".js")
            {
                var path = Path.GetRelativePath(root, module.FileName);

                if (context.JsFragments.TryGetValue(module, out var fragment))
                {
                    var imports = new List<InputImportDefinition>();
                    foreach (var (astNode, resolvedNode) in fragment.Replacements)
                    {
                        var original = astNode switch
                        {
                            Syntax.Ast.ImportDeclaration import => import.Source.Value,
                            Syntax.Ast.ImportExpression dyn => (dyn.Source as Syntax.Ast.StringLiteral)?.Value ?? "",
                            Syntax.Ast.CallExpression call =>
                                call.Arguments.Count > 0 && call.Arguments[0] is Syntax.Ast.StringLiteral str
                                    ? str.Value : "",
                            Syntax.Ast.ExportNamedDeclaration exp => exp.Source?.Value ?? "",
                            Syntax.Ast.ExportAllDeclaration expAll => expAll.Source.Value,
                            _ => "",
                        };

                        imports.Add(new InputImportDefinition
                        {
                            Kind = "import-statement",
                            Original = original,
                            Path = Path.GetRelativePath(root, resolvedNode.FileName),
                        });
                    }

                    container.Inputs[path] = new InputNode
                    {
                        Format = "esm",
                        Bytes = module.Bytes,
                        Imports = imports,
                    };
                }
            }
        }

        // Build outputs section: every bundle and asset
        var fileSizes = emitted.ToDictionary(e => e.Name, e => e.Size);

        foreach (var bundle in context.Bundles.Values)
        {
            var path = bundle.GetFileName();
            fileSizes.TryGetValue(path, out var fileSize);

            var items = bundle.Items.Where(m => m.Type == ".js").ToList();
            var total = Math.Max(1, items.Sum(m => m.Bytes));

            var inputs = new Dictionary<string, InputDefinition>();
            foreach (var item in items)
            {
                inputs[Path.GetRelativePath(root, item.FileName)] = new InputDefinition
                {
                    BytesInOutput = (int)(fileSize * item.Bytes / total),
                };
            }

            // Collect cross-bundle references (JS→CSS, shared deps)
            var imports = new List<OutputImportDefinition>();
            foreach (var item in bundle.Items)
            {
                foreach (var child in item.Children)
                {
                    if (child.Type == ".css" && nodeToOutput.TryGetValue(child, out var cssOutput))
                    {
                        imports.Add(new OutputImportDefinition
                        {
                            Kind = "import-statement",
                            Path = cssOutput,
                        });
                    }
                }
            }

            container.Outputs[path] = new OutputNode
            {
                Bytes = (int)fileSize,
                Exports = [],
                Imports = imports,
                Inputs = inputs,
                EntryPoint = bundle.IsPrimary ? path : null,
                Flags = bundle.IsPrimary ? "entry" : "shared",
            };
        }

        // Add non-bundle assets (images, fonts, etc.)
        foreach (var asset in context.Assets.Values)
        {
            var path = asset.GetFileName();
            if (!fileSizes.TryGetValue(path, out var size)) continue;

            container.Outputs[path] = new OutputNode
            {
                Bytes = (int)size,
                Exports = [],
                Imports = [],
                Inputs = [],
                EntryPoint = null,
                Flags = null,
            };
        }

        return System.Text.Json.JsonSerializer.Serialize(container, NetPack.Json.SourceGenerationContext.Default.MetadataContainer);
    }

    public void Dispose()
    {
        _njs.Dispose();
        ((IDisposable)_browser).Dispose();
    }
}
