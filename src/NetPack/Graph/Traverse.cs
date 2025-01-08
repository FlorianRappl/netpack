namespace NetPack.Graph;

using System.Text.Json;
using System.Text.Unicode;
using Acornima;
using Acornima.Jsx;
using AngleSharp;
using AngleSharp.Css.Parser;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using NetPack.Graph.Visitors;
using static NetPack.Helpers;

public class Traverse
{
    private readonly BundlerContext _context;
    private readonly BrowsingContext _browser;

    public Traverse()
    {
        _context = new BundlerContext();
        _browser = new BrowsingContext(Configuration.Default.WithCss());
    }

    public BundlerContext Context => _context;

    public static Task<Traverse> From(string path) => From(path, [], []);

    public static async Task<Traverse> From(string path, IEnumerable<string> externals, IEnumerable<string> shared)
    {
        var traverse = new Traverse();
        traverse.Context.Externals = [.. externals, .. shared];
        traverse.Context.Shared = [.. shared];
        await traverse.Start(path);
        return traverse;
    }

    private async Task Start(string path)
    {
        var entry = await Resolve(Environment.CurrentDirectory, path);
        await AddNewBundle(entry);
        Populate();
    }

    private void Populate()
    {
        var bundles = _context.Bundles;
        var nodes = bundles.Select(m => m.Root);
        var connected = new Connected((i, nodes) => $"common.{i:0000}{nodes.First().Type}");
        var graphs = connected.Apply(nodes);

        foreach (var graph in graphs)
        {
            var bundle = bundles.FirstOrDefault(m => m.Root == graph.Key);

            if (bundle is null)
            {
                bundle = CreateBundle(graph.Key, BundleFlags.Shared);
                bundles.Add(bundle);
            }

            bundle.Items = [.. graph.Value];
        }
    }

    private Bundle CreateBundle(Node root, BundleFlags flags)
    {
        return root.Type switch
        {
            ".html" => new HtmlBundle(_context, root, flags),
            ".js" => new JsBundle(_context, root, flags),
            ".css" => new CssBundle(_context, root, flags),
            _ => throw new NotSupportedException($"No bundle for type '{root.Type}' found."),
        };
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
            Console.WriteLine("Error from {1}! {0}", err, parent.FileName);
            return null;
        }
    }

    private async Task ProcessAsset(Node current, byte[] bytes, Bundle bundle)
    {
        using var stream = new MemoryStream(bytes);
        var hash = await Hash.ComputeHash(stream);
        _context.Assets.Add(new Asset(current, current.Type, bytes, hash));
    }

    private Task ProcessJson(Node current, byte[] bytes, Bundle bundle)
    {
        // nothing on purpose
        return Task.CompletedTask;
    }

    private async Task ProcessStyleSheet(Node current, byte[] bytes, Bundle bundle)
    {
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

    private async Task ProcessJavaScript(Node current, byte[] bytes, Bundle bundle)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var parser = new JsxParser(new JsxParserOptions
        {
            Tolerant = true,
            AllowAwaitOutsideFunction = true,
            JsxAllowNamespaces = true,
        });
        var newContent = content
            .Replace("process.env.NODE_ENV", "'production'")
            .Replace(": ChangeEvent<HTMLInputElement>", ""); // this should be removed; just for now.
        var ast = parser.ParseModule(newContent, current.FileName);
        var visitor = new JsVisitor(bundle, current, InnerProcess);
        var fragment = await visitor.FindChildren(ast);
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
            _context.Assets.Add(new Asset(node, node.Type, bytes));
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
            _context.JsFragments.TryAdd(node, new JsExternalFragment(node));
        }

        parent.Children.Add(node);
        return node;
    }

    private Task<Node> AddNewBundle(string fileName) => AddToBundle(null, fileName);

    private async Task<Node> AddToBundle(Bundle? bundle, string fileName)
    {
        if (!_context.Modules.TryGetValue(fileName, out var node))
        {
            var bytes = await File.ReadAllBytesAsync(fileName);
            node = new Node(fileName, bytes.Length);
            _context.Modules.TryAdd(fileName, node);

            if (bundle is null)
            {
                var flags = _context.Bundles.Count == 0 ? BundleFlags.Primary : BundleFlags.None;
                bundle = CreateBundle(node, flags);
                _context.Bundles.Add(bundle);
            }

            await (node.Type switch
            {
                ".js" => ProcessJavaScript(node, bytes, bundle),
                ".html" => ProcessHtml(node, bytes, bundle),
                ".css" => ProcessStyleSheet(node, bytes, bundle),
                ".json" => ProcessJson(node, bytes, bundle),
                _ => ProcessAsset(node, bytes, bundle),
            });
        }

        return node;
    }
}
