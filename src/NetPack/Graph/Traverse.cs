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

    // The dev server builds in development mode (React dev warnings, Fast
    // Refresh); production bundles inline the production NODE_ENV.
    private string NodeEnvLiteral => _devServer ? "'development'" : "'production'";

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

    public static async Task<Traverse> From(string path, IEnumerable<string> externals, IEnumerable<string> shared, ModuleIdMap? moduleIds = null, bool devServer = false)
    {
        var root = Path.GetDirectoryName(path)!;
        var packageRoot = FindRoot(root);
        var features = await FindFeatures(packageRoot);
        var traverse = new Traverse(packageRoot ?? root, features, moduleIds) { _devServer = devServer };
        var (jsxFactory, jsxFragmentFactory) = await FindJsxFactories(packageRoot);
        traverse.Context.JsxFactory = jsxFactory;
        traverse.Context.JsxFragmentFactory = jsxFragmentFactory;
        traverse.Context.Externals = [.. externals, .. shared];
        traverse.Context.Shared = [.. shared];
        await traverse.Run([path, .. shared]);
        return traverse;
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
                // special case - Module Federation
                case "federation.json":
                    await AddModuleFederation(entry);
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
        while (currentDir is not null)
        {
            var nodeModulesPath = CombinePath(currentDir, "node_modules", packageName);

            if (Directory.Exists(nodeModulesPath))
            {
                var packageJsonPath = CombinePath(nodeModulesPath, "package.json");

                if (File.Exists(packageJsonPath))
                {
                    var mainEntry = await GetMainEntryFromPackageJson(packageJsonPath);

                    if (File.Exists(mainEntry))
                    {
                        return mainEntry;
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

    private async Task<string> GetMainEntryFromPackageJson(string packageJsonPath)
    {
        var dependency = _context.Dependencies.FirstOrDefault(m => m.Location == packageJsonPath);

        if (dependency is null)
        {
            using var packageJson = File.OpenRead(packageJsonPath);
            var jsonDoc = await JsonDocument.ParseAsync(packageJson);
            var jsonObj = jsonDoc.RootElement;

            dependency = new Dependency(packageJsonPath, jsonObj);

            if (!_context.Dependencies.Any(m => m.Location == packageJsonPath))
            {
                _context.Dependencies.Add(dependency);
            }
        }

        return dependency.Entry;
    }

    private async Task<Node?> InnerProcess(Bundle? bundle, Node parent, string name)
    {
        if (_context.Aliases.TryGetValue(name, out var alias))
        {
            return await InnerProcess(bundle, parent, alias);
        }

        if (_context.Externals.Contains(name))
        {
            return AddExternalReference(parent, name);
        }

        if (name.StartsWith("//") || name.StartsWith("file:") || name.StartsWith("http:") || name.StartsWith("https:"))
        {
            // ignore URLs
            return null;
        }

        try
        {
            var file = await Resolve(parent.ParentDir, name);
            var module = await AddToBundle(bundle, file);

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

    private async Task ProcessAsset(Node current, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var hash = await Hash.ComputeHash(stream);
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

        var template = await ReadVueBlock(current, templateEl, isTemplate: true);
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

        var descriptor = new VueDescriptor
        {
            Template = template,
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

        var factory = pragma.Factory ?? (isTypeScript ? _context.JsxFactory : null);
        if (!string.IsNullOrEmpty(factory))
        {
            fragment.JsxFactory = factory;
        }

        var fragmentFactory = pragma.FragmentFactory ?? (isTypeScript ? _context.JsxFragmentFactory : null);
        if (!string.IsNullOrEmpty(fragmentFactory))
        {
            fragment.JsxFragmentFactory = fragmentFactory;
        }
    }

    private async Task ProcessJavaScript(Node current, byte[] bytes, Bundle bundle)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // TypeScript is stripped natively by the parser (see ParserOptions.ForFile),
        // so .ts/.tsx no longer need an external `tsc` pass. The one remaining
        // source transform is the build-time NODE_ENV define.
        var newContent = content.Replace("process.env.NODE_ENV", NodeEnvLiteral);
        var fragment = await ParseJsModule(bundle, current, newContent);
        _context.JsFragments.TryAdd(current, fragment);
    }

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

    private async Task<Node> AddModuleFederation(string entry)
    {
        var definition = await ModuleFederationHelpers.ReadFrom(entry);

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

    private async Task<Node> AddToBundle(Bundle? bundle, string fileName)
    {
        if (!_context.Modules.TryGetValue(fileName, out var node))
        {
            if (_reserved.TryGetValue(fileName, out var task))
            {
                return await task;
            }

            node = await _reserved.GetOrAdd(fileName, (_) => AddNewNodeToBundle(bundle, fileName));
            _reserved.TryRemove(fileName, out _);
        }

        return node;
    }

    private async Task<Node> AddNewNodeToBundle(Bundle? bundle, string fileName)
    {
        var bytes = await File.ReadAllBytesAsync(fileName);
        var node = new Node(fileName, bytes.Length);
        _context.Modules.TryAdd(fileName, node);

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
