namespace NetPack.Graph;

using System.Text.Json;
using NetPack.Graph.Writers;
using NetPack.Json;
using NetPack.Server;

class Metadata(Traverse graph, MemoryResultWriter compilation) : IFileLocator
{
    private readonly Traverse _graph = graph;
    private readonly MemoryResultWriter _compilation = compilation;

    bool IFileLocator.HasFile(string fullPath)
    {
        return ((IFileLocator)_compilation).HasFile(fullPath);
    }

    public string Stringify()
    {
        var root = Environment.CurrentDirectory;
        var context = _graph.Context;
        var container = new MetadataContainer
        {
            Inputs = [],
            Outputs = []
        };

        foreach (var module in context.Modules.Values)
        {
            if (module.Type == ".js")
            {
                var path = Path.GetRelativePath(root, module.FileName);
                var format = "esm";

                if (context.JsFragments.TryGetValue(module, out var fragment))
                {
                    var imports = fragment.Replacements.Values.Select(m => new InputImportDefinition
                    {
                        Kind = "import-statement",
                        Original = "",
                        Path = Path.GetRelativePath(root, m.FileName),
                    }).ToList();

                    container.Inputs[path] = new InputNode
                    {
                        Format = format,
                        Bytes = module.Bytes,
                        Imports = imports,
                    };
                }
            }
        }

        foreach (var bundle in _graph.Context.Bundles.Values)
        {
            var path = bundle.GetFileName();
            var file = _compilation.GetFile(path);

            if (bundle.Type == ".js" && file is not null)
            {
                var items = bundle.Items.Where(m => m.Type == ".js");
                var total = Math.Max(1, items.Sum(m => m.Bytes));
                var inputs = new Dictionary<string, InputDefinition>();
                var exports = new List<OutputExportDefinition>();
                var imports = new List<OutputImportDefinition>();
                
                foreach (var item in items)
                {
                    inputs[Path.GetRelativePath(root, item.FileName)] = new InputDefinition
                    {
                        BytesInOutput = file.Length * item.Bytes / total,
                    };
                }

                container.Outputs[path] = new OutputNode
                {
                    Bytes = file.Length,
                    Exports = exports,
                    Imports = imports,
                    Inputs = inputs,
                };
            }
        }

        return JsonSerializer.Serialize(container, SourceGenerationContext.Default.MetadataContainer);
    }
}
