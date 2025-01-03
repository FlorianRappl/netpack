namespace NetPack.Graph;

using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Acornima.Jsx;
using AngleSharp;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using NetPack.Chunks;
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
        Populate(traverse.Context);
        return traverse;
    }

    private static void Populate(BundlerContext context)
    {
        var bundles = context.Bundles;
        var nodes = bundles.Select(m => m.Root);
        var connected = new Connected(i => $"common.{i:0000}.js");
        var graphs = connected.Apply(nodes);

        foreach (var graph in graphs)
        {
            var bundle = bundles.FirstOrDefault(m => m.Root == graph.Key);

            if (bundle is null)
            {
                bundle = new Bundle(graph.Key);
                bundles.Add(bundle);
            }

            bundle.Items.AddRange(graph.Value);
        }
    }

    private async Task Start(string path)
    {
        var entry = await Resolve(Environment.CurrentDirectory, path);
        var root = await AddToBundle(entry);
        _context.Bundles.Add(new Bundle(root));
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

    private async Task<Node?> InnerProcess(Node parent, string name, bool isReference = false)
    {
        try
        {
            var file = await Resolve(parent.ParentDir, name);
            var module = await AddToBundle(file);

            if (isReference)
            {
                parent.References.Add(module);
                _context.Bundles.Add(new Bundle(module));
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

    private async Task ProcessAsset(Node current)
    {
        using var stream = File.OpenRead(current.FileName);
        var hash = await Hash.ComputeHash(stream);
        _context.Assets.Add(new Asset(current, current.Type, hash));
    }

    private Task ProcessJson(Node current)
    {
        // nothing on purpose
        return Task.CompletedTask;
    }

    private async Task ProcessStyleSheet(Node current)
    {
        using var content = File.OpenRead(current.FileName);
        var tasks = new List<Task>();
        var options = new CssParserOptions
        {
            IsIncludingUnknownRules = true,
            IsIncludingUnknownDeclarations = true,
            IsToleratingInvalidSelectors = true,
        };
        var parser = new CssParser(options, _browser);
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
                        tasks.Add(InnerProcess(current, path, false));
                    }
                }
            }
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessJavaScript(Node current)
    {
        var content = await File.ReadAllTextAsync(current.FileName);
        var parser = new JsxParser(new JsxParserOptions
        {
            Tolerant = true,
            AllowAwaitOutsideFunction = true,
            JsxAllowNamespaces = true,
        });
        var tasks = new List<Task<Node?>>();
        var ast = parser.ParseModule(content.Replace(": ChangeEvent<HTMLInputElement>", ""), current.FileName);
        var elements = new List<Acornima.Ast.Node>();

        foreach (var node in ast.DescendantNodes())
        {
            switch (node.Type)
            {
                case NodeType.ImportDeclaration:
                    {
                        var file = ((ImportDeclaration)node).Source.Value;
                        elements.Add(node);
                        tasks.Add(InnerProcess(current, file, false));
                        break;
                    }
                case NodeType.ExportAllDeclaration:
                    {
                        var file = ((ExportAllDeclaration)node).Source.Value;
                        elements.Add(node);
                        tasks.Add(InnerProcess(current, file, false));
                        break;
                    }
                case NodeType.ExportNamedDeclaration:
                    {
                        var str = ((ExportNamedDeclaration)node).Source;

                        if (str is not null)
                        {
                            elements.Add(node);
                            tasks.Add(InnerProcess(current, str.Value, false));
                        }

                        break;
                    }
                case NodeType.ImportExpression:
                    {
                        if (((ImportExpression)node).Source is StringLiteral str)
                        {
                            elements.Add(node);
                            tasks.Add(InnerProcess(current, str.Value, true));
                        }

                        break;
                    }
                case NodeType.CallExpression:
                    {
                        var call = (CallExpression)node;

                        if (call.Callee is Identifier ident && call.Arguments.Count == 1 && call.Arguments[0] is StringLiteral str && ident.Name == "require")
                        {
                            elements.Add(node);
                            tasks.Add(InnerProcess(current, str.Value, false));
                        }

                        break;
                    }
            }
        }

        var nodes = await Task.WhenAll(tasks);
        var replacements = elements.Select((r, i) => (nodes[i], r)).ToArray();
        var chunk = new JsChunk(ast, replacements);
        _context.Chunks.TryAdd(current, chunk);
    }

    private async Task ProcessHtml(Node current)
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
                tasks.Add(InnerProcess(current, src, true));
            }
        }

        foreach (var element in document.QuerySelectorAll("link,a"))
        {
            var href = element.GetAttribute("href");

            if (href is not null)
            {
                elements.Add(element);
                tasks.Add(InnerProcess(current, href, true));
            }
        }

        var nodes = await Task.WhenAll(tasks);
        var replacements = elements.Select((r, i) => (nodes[i], r)).ToArray();
        var chunk = new HtmlChunk(document, replacements);
        _context.Chunks.TryAdd(current, chunk);
    }

    private async Task<Node> AddToBundle(string fileName)
    {
        if (_context.Modules.TryGetValue(fileName, out var value))
        {
            return value;
        }

        var node = new Node(fileName);
        _context.Modules.TryAdd(fileName, node);

        await (node.Type switch
        {
            ".js" => ProcessJavaScript(node),
            ".html" => ProcessHtml(node),
            ".css" => ProcessStyleSheet(node),
            ".json" => ProcessJson(node),
            _ => ProcessAsset(node),
        });

        return node;
    }
}
