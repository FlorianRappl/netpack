namespace NetPack.Graph;

using System.Text.Json;
using System.Text.RegularExpressions;

using static NetPack.Helpers;

public class Traverse
{
    private static readonly Regex htmlLink = Expressions.HtmlLink();
    private static readonly Regex htmlScript = Expressions.HtmlScript();
    private static readonly Regex cssImport = Expressions.CssImport();
    private static readonly Regex jsAsync = Expressions.JsAsyncImport();
    private static readonly Regex jsSync = Expressions.JsSyncImport();
    private static readonly Regex jsRequire = Expressions.JsRequire();
    private static readonly Regex jsExport = Expressions.JsSyncExport();

    private readonly BundlerContext _context;

    private Traverse()
    {
        _context = new BundlerContext();
    }

    public BundlerContext Context => _context;

    public Node? Root { get; private set;}

    public static async Task<Traverse> From(string path)
    {
        var traverse = new Traverse();
        await traverse.Start(path);
        return traverse;
    }

    private async Task Start(string path)
    {
        var entry = await Resolve(Environment.CurrentDirectory, path);
        Root = await AddToBundle(entry);
    }

    private async Task<string> Resolve(string dir, string name)
    {
        if (!name.StartsWith(".") && !Path.IsPathFullyQualified(name))
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

    async Task<string?> ResolveFromNodeModules(string? currentDir, string packageName)
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

    async Task<string> GetMainEntryFromPackageJson(string packageJsonPath)
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

    async Task<string> ReadFile(string fileName)
    {
        using var sr = File.OpenText(fileName);
        return await sr.ReadToEndAsync();
    }

    private async Task InnerProcess(Node parent, string name, bool isReference = false)
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
                _context.Bundles.FirstOrDefault(m => m.Items.Contains(parent))?.Items.Add(module);
            }
        }
        catch
        {
            // Do nothing
        }
    }

    private Task ProcessAsset(Node current)
    {
        _context.Assets.Add(new Asset(current, current.Type));
        return Task.CompletedTask;
    }

    private async Task ProcessStyleSheet(Node current)
    {
        var content = await ReadFile(current.FileName);
        var importMatches = cssImport!.Matches(content);

        await Task.WhenAll(importMatches.Select((match) => InnerProcess(current, match.Groups[match.Groups[1].Success ? 1 : 2].Value, true)));
    }

    private async Task ProcessJavaScript(Node current)
    {
        var content = await ReadFile(current.FileName);
        var asyncMatches = jsAsync!.Matches(content);
        var syncMatches = jsSync!.Matches(content);
        var requireMatches = jsRequire!.Matches(content);
        var exportMatches = jsExport!.Matches(content);

        await Task.WhenAll(
            Task.WhenAll(asyncMatches.Select((match) => InnerProcess(current, match.Groups[2].Value, true))),
            Task.WhenAll(syncMatches.Select((match) => InnerProcess(current, match.Groups[4].Value, false))),
            Task.WhenAll(requireMatches.Select((match) => InnerProcess(current, match.Groups[2].Value, false))),
            Task.WhenAll(exportMatches.Select((match) => InnerProcess(current, match.Groups[2].Value, false)))
        );
    }

    async Task ProcessHtml(Node current)
    {
        var content = await ReadFile(current.FileName);
        var linkMatches = htmlLink!.Matches(content);
        var scriptMatches = htmlScript!.Matches(content);

        await Task.WhenAll(
            Task.WhenAll(linkMatches.Select((match) => InnerProcess(current, match.Groups[1].Value, true))),
            Task.WhenAll(scriptMatches.Select((match) => InnerProcess(current, match.Groups[1].Value, true)))
        );

        _context.Assets.Add(new Asset(current, current.Type));
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
            _ => ProcessAsset(node),
        });

        return node;
    }
}
