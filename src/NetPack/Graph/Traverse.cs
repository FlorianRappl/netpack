namespace NetPack.Graph;

using System.Text.Json;
using Acornima;
using Acornima.Jsx;
using AngleSharp;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using NetPack.Fragments;
using static NetPack.Helpers;

public class Traverse
{
    private readonly BundlerContext _context;
    private readonly BrowsingContext _browser;

    private Traverse()
    {
        _context = new BundlerContext();
        _browser = new BrowsingContext(Configuration.Default.WithCss());
    }

    public BundlerContext Context => _context;

    public static async Task<Traverse> From(string path)
    {
        var traverse = new Traverse();
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

            bundle.Items.AddRange(graph.Value);
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

    private async Task ProcessAsset(Node current, Bundle bundle)
    {
        using var stream = File.OpenRead(current.FileName);
        var hash = await Hash.ComputeHash(stream);
        _context.Assets.Add(new Asset(current, current.Type, hash));
    }

    private Task ProcessJson(Node current, Bundle bundle)
    {
        // nothing on purpose
        return Task.CompletedTask;
    }

    private async Task ProcessStyleSheet(Node current, Bundle bundle)
    {
        using var content = File.OpenRead(current.FileName);
        var tasks = new List<Task<Node?>>();
        var options = new CssParserOptions
        {
            IsIncludingUnknownRules = true,
            IsIncludingUnknownDeclarations = true,
            IsToleratingInvalidSelectors = true,
        };
        var parser = new CssParser(options, _browser);
        var properties = new List<ICssProperty>();
        var sheet = await parser.ParseStyleSheetAsync(content);

        foreach (var rule in sheet.Rules)
        {
            if (rule is ICssStyleRule style)
            {
                foreach (var decl in style.Style)
                {
                    var path = decl.RawValue.AsUrl();

                    if (path is not null)
                    {
                        properties.Add(decl);
                        tasks.Add(InnerProcess(bundle, current, path));
                    }
                }
            }
        }

        var nodes = await Task.WhenAll(tasks);
        var replacements = properties.Select((r, i) => (nodes[i]!, r)).ToDictionary(m => m.r, m => m.Item1);
        CssBundle.Fragments.Add(new CssFragment(current, sheet, replacements));
    }

    private async Task ProcessJavaScript(Node current, Bundle bundle)
    {
        var content = await File.ReadAllTextAsync(current.FileName);
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
        JsBundle.Fragments.Add(fragment);
    }

    private async Task ProcessHtml(Node current, Bundle bundle)
    {
        using var content = File.OpenRead(current.FileName);
        var tasks = new List<Task<Node?>>();
        var document = await _browser.OpenAsync(res => res.Content(content));
        var elements = new List<AngleSharp.Dom.IElement>();

        foreach (var element in document.QuerySelectorAll("img,script,audio,video"))
        {
            var src = element.GetAttribute("src");

            if (src is not null)
            {
                elements.Add(element);
                tasks.Add(InnerProcess(null, current, src));
            }
        }

        foreach (var element in document.QuerySelectorAll("link,a"))
        {
            var href = element.GetAttribute("href");

            if (href is not null)
            {
                elements.Add(element);
                tasks.Add(InnerProcess(null, current, href));
            }
        }

        var nodes = await Task.WhenAll(tasks);
        var replacements = elements.Select((r, i) => (nodes[i]!, r)).ToDictionary(m => m.r, m => m.Item1);
        HtmlBundle.Fragments.Add(new HtmlFragment(current, document, replacements));
    }

    private Task<Node> AddNewBundle(string fileName) => AddToBundle(null, fileName);

    private async Task<Node> AddToBundle(Bundle? bundle, string fileName)
    {
        if (!_context.Modules.TryGetValue(fileName, out var node))
        {
            node = new Node(fileName);
            _context.Modules.TryAdd(fileName, node);

            if (bundle is null)
            {
                var flags = _context.Bundles.Count == 0 ? BundleFlags.Primary : BundleFlags.None;
                bundle = CreateBundle(node, flags);
                _context.Bundles.Add(bundle);
            }

            await (node.Type switch
            {
                ".js" => ProcessJavaScript(node, bundle),
                ".html" => ProcessHtml(node, bundle),
                ".css" => ProcessStyleSheet(node, bundle),
                ".json" => ProcessJson(node, bundle),
                _ => ProcessAsset(node, bundle),
            });
        }

        return node;
    }
}
