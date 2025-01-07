namespace NetPack.Graph.Visitors;

using System.Text.Json;
using AngleSharp.Dom;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using static NetPack.Helpers;

class HtmlVisitor(Bundle bundle, Graph.Node current, Func<Bundle?, Graph.Node, string, Task<Graph.Node?>> report, Action<string> addExternal)
{
    private readonly Func<Bundle?, Graph.Node, string, Task<Graph.Node?>> _report = report;
    private readonly Action<string> _addExternal = addExternal;
    private readonly Bundle _bundle = bundle;
    private readonly Graph.Node _current = current;
    private readonly List<IElement> _elements = [];
    private readonly List<Task<Graph.Node?>> _tasks = [];

    public async Task<HtmlFragment> FindChildren(IDocument document)
    {
        foreach (var element in document.QuerySelectorAll("img,script,audio,video"))
        {
            var src = element.GetAttribute("src");

            if (src is not null)
            {
                _elements.Add(element);
                _tasks.Add(_report(null, _current, src));
            }

            if (element.LocalName == "script" && element.GetAttribute("type") == "importmap")
            {
                var source = element.TextContent;

                try
                {
                    var importmap = JsonSerializer.Deserialize(source, SourceGenerationContext.Default.Importmap);

                    if (importmap?.Imports is not null)
                    {
                        foreach (var name in importmap.Imports.Keys)
                        {
                            _addExternal(name);
                        }
                    }
                }
                catch
                {
                    // Ignore importmap issues
                }
            }
        }

        foreach (var element in document.QuerySelectorAll("link,a"))
        {
            var href = element.GetAttribute("href");

            if (href is not null)
            {
                _elements.Add(element);
                _tasks.Add(_report(null, _current, href));
            }
        }

        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _elements);
        return new HtmlFragment(_current, document, replacements);
    }
}