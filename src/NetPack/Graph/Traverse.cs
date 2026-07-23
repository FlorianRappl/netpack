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
using NetPack.Json;
using NetPack.Syntax;
using static NetPack.Helpers;

public class Traverse(string root, FeatureFlags features, ModuleIdMap? moduleIds = null) : IDisposable
{
    private readonly BundlerContext _context = new(root, features, moduleIds);
    private readonly BrowsingContext _browser = new(Configuration.Default.WithCss());
    private readonly ConcurrentDictionary<string, Task<Node>> _reserved = [];
    private readonly NodeJs _njs = new(root);
    private bool _devServer;

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

    public static async Task<Traverse> From(string path, IEnumerable<string> externals, IEnumerable<string> shared, ModuleIdMap? moduleIds = null, bool devServer = false, Platform platform = Platform.Web, IReadOnlyDictionary<string, string>? defines = null, IReadOnlyDictionary<string, string>? aliases = null, IReadOnlyDictionary<string, string>? loaders = null, IEnumerable<string>? conditions = null, bool externalPackages = false)
    {
        var root = Path.GetDirectoryName(path)!;
        var packageRoot = FindRoot(root);
        var features = await FindFeatures(packageRoot);
        var traverse = new Traverse(packageRoot ?? root, features, moduleIds) { _devServer = devServer };
        traverse.Context.Platform = PlatformTargets.For(platform);
        traverse.Context.Defines = BuildDefines(defines, devServer);
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
        traverse.Context.Externals = [.. externals, .. shared];
        traverse.Context.Shared = [.. shared];
        await traverse.Run([path, .. shared]);
        return traverse;
    }

    /// <summary>
    /// Builds the effective <c>--define</c> table: the built-in
    /// <c>process.env.NODE_ENV</c> default (development on the dev server,
    /// production otherwise) overlaid with the user's entries, then ordered
    /// longest-key-first for safe sequential text replacement.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string>> BuildDefines(IReadOnlyDictionary<string, string>? defines, bool devServer)
    {
        var map = new Dictionary<string, string>
        {
            ["process.env.NODE_ENV"] = devServer ? "'development'" : "'production'",
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
        await TransformCssModules();

        if (_devServer && primaryEntry is not null)
        {
            await SetupReactRefresh(primaryEntry);
        }

        Finish();
    }

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
        var bundles = _context.Bundles;
        var connected = new Connected((i, nodes) => $"common.{i:0000}{nodes.First().Type}");
        var graphs = connected.Apply(bundles.Keys);

        foreach (var graph in graphs)
        {
            if (!bundles.TryGetValue(graph.Key, out var bundle))
            {
                bundle = CreateBundle(graph.Key, BundleFlags.Shared);
                bundles.TryAdd(graph.Key, bundle);
            }

            bundle.Items = [.. graph.Value];
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

        return ResolveFromFileSystem(CombinePath(dir, name)) ?? throw new Exception($"Could not find the module '{name}' in '{dir}'.");
    }

    private string? ResolveFromFileSystem(string fn)
    {
        if (Directory.Exists(fn))
        {
            fn = CombinePath(fn, "index");
        }

        var files = Directory.GetFiles(Path.GetDirectoryName(fn)!);

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

        // Runtime built-ins for the target platform (e.g. `node:fs` / `fs` on Node,
        // `npm:`/`jsr:` on Deno) are provided by the runtime — keep them external.
        if (_context.Platform.IsBuiltin(name))
        {
            return AddExternalReference(parent, name);
        }

        if (name.StartsWith("//") || name.StartsWith("file:") || name.StartsWith("http:") || name.StartsWith("https:"))
        {
            // ignore URLs
            return null;
        }

        // With --packages=external, every bare (node_modules) import is kept
        // external — a relative or absolute path is still bundled as usual.
        if (_context.ExternalPackages && !name.StartsWith('.') && !Path.IsPathRooted(name))
        {
            return AddExternalReference(parent, name);
        }

        // Split off a trailing `?...` query string (irrelevant for locating the
        // file itself) and, from it, any `width=`/`height=`/`format=` params —
        // this is how a JS/TS import requests an image variant, e.g.
        // `import img from './logo.png?width=200&height=100&format=webp'`. An
        // explicitly passed-in `variant` (from an HTML <img> width/height
        // attribute or a CSS background-size) wins if both are somehow present.
        var (path, queryVariant) = ParseVariantQuery(name);
        var width = variant.Width ?? queryVariant.Width;
        var height = variant.Height ?? queryVariant.Height;
        var format = variant.Format ?? queryVariant.Format;

        try
        {
            var file = await Resolve(parent.ParentDir, path);
            var module = await AddToBundle(bundle, file, width, height, format);

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
            Console.WriteLine("Error from '{0}': {1}", parent.FileName, err.Message);
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
    /// variant. Any other query params are accepted and silently ignored
    /// (resolution only ever needs the part before the `?`).
    /// </summary>
    private static (string Path, (int? Width, int? Height, string? Format) Variant) ParseVariantQuery(string name)
    {
        var index = name.IndexOf('?');

        if (index < 0)
        {
            return (name, default);
        }

        var path = name[..index];
        var query = name[(index + 1)..];
        int? width = null;
        int? height = null;
        string? format = null;

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
        }

        return (path, (width, height, format));
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
        var visitor = new JsVisitor(bundle, current, InnerProcess);
        var fragment = await visitor.FindChildren(ast);
        ApplyJsxFactory(current, content, options.TypeScript, fragment);
        RegisterCssImports(bundle, fragment);
        return fragment;
    }

    /// <summary>
    /// Records CSS files this module imports so they can later be turned into
    /// virtual JS modules. An import that carries named/default bindings marks the
    /// CSS file as a CSS module (its class names get hashed).
    /// </summary>
    private void RegisterCssImports(Bundle bundle, JsFragment fragment)
    {
        foreach (var (astNode, graphNode) in fragment.Replacements)
        {
            if (astNode is Syntax.Ast.ImportDeclaration import && graphNode.Type == ".css")
            {
                _context.CssImports.TryAdd(graphNode, bundle);

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
    /// </summary>
    private async Task TransformCssModules()
    {
        foreach (var (node, bundle) in _context.CssImports.ToArray())
        {
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

        var fragment = await ParseJsModule(bundle, current, newContent);
        _context.JsFragments.TryAdd(current, fragment);
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
                var dataUrl = $"data:{GetMimeType(current.Extension)};base64,{Convert.ToBase64String(bytes)}";
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

    private static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".json" => "application/json",
        ".txt" => "text/plain",
        ".css" => "text/css",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream",
    };

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
    private static string GetModuleKey(string fileName, int? variantWidth, int? variantHeight, string? variantFormat)
        => variantWidth is null && variantHeight is null && variantFormat is null
            ? fileName
            : $"{fileName}?w={variantWidth}&h={variantHeight}&f={variantFormat}";

    private async Task<Node> AddToBundle(Bundle? bundle, string fileName, int? variantWidth = null, int? variantHeight = null, string? variantFormat = null)
    {
        var key = GetModuleKey(fileName, variantWidth, variantHeight, variantFormat);

        if (!_context.Modules.TryGetValue(key, out var node))
        {
            if (_reserved.TryGetValue(key, out var task))
            {
                return await task;
            }

            node = await _reserved.GetOrAdd(key, (_) => AddNewNodeToBundle(bundle, fileName, variantWidth, variantHeight, variantFormat));
            _reserved.TryRemove(key, out _);
        }

        return node;
    }

    private async Task<Node> AddNewNodeToBundle(Bundle? bundle, string fileName, int? variantWidth = null, int? variantHeight = null, string? variantFormat = null)
    {
        var bytes = await File.ReadAllBytesAsync(fileName);
        var node = new Node(fileName, bytes.Length, variantWidth, variantHeight, variantFormat);
        _context.Modules.TryAdd(GetModuleKey(fileName, variantWidth, variantHeight, variantFormat), node);

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

    public void Dispose()
    {
        _njs.Dispose();
        ((IDisposable)_browser).Dispose();
    }
}
