namespace NetPack.Graph.Visitors;

using System.Text.Json;
using AngleSharp.Dom;
using NetPack.Fragments;
using NetPack.Graph.Bundles;
using NetPack.Json;
using static NetPack.Helpers;

class HtmlVisitor(Bundle bundle, Graph.Node current, Func<Bundle?, Graph.Node, string, (int? Width, int? Height, string? Format), Task<Graph.Node?>> report, Action<string> addExternal)
{
    private readonly Func<Bundle?, Graph.Node, string, (int? Width, int? Height, string? Format), Task<Graph.Node?>> _report = report;
    private readonly Action<string> _addExternal = addExternal;
    private readonly Bundle _bundle = bundle;
    private readonly Graph.Node _current = current;
    private readonly List<IElement> _elements = [];
    private readonly List<Task<Graph.Node?>> _tasks = [];

    public async Task<HtmlFragment> FindChildren(IDocument document)
    {
        foreach (var element in document.All)
        {
            switch (element.LocalName)
            {
                case "img":
                    {
                        var src = element.GetAttribute("src");

                        if (src is not null)
                        {
                            _elements.Add(element);
                            _tasks.Add(_report(null, _current, src, GetImageVariant(element)));
                        }
                    }

                    break;
                case "iframe":
                case "source":
                case "audio":
                case "video":
                    {
                        var src = element.GetAttribute("src");

                        if (src is not null)
                        {
                            _elements.Add(element);
                            _tasks.Add(_report(null, _current, src, default));
                        }
                    }

                    break;
                case "script":
                    {
                        var src = element.GetAttribute("src");

                        if (src is not null)
                        {
                            _elements.Add(element);
                            _tasks.Add(_report(null, _current, src, default));
                        }

                        if (element.GetAttribute("type") == "importmap")
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

                            break;
                        }
                    }

                    break;
                case "link":
                case "a":
                    {
                        var href = element.GetAttribute("href");

                        if (href is not null)
                        {
                            _elements.Add(element);
                            _tasks.Add(_report(null, _current, href, default));
                        }
                    }

                    break;
                case "object":
                    {
                        var href = element.GetAttribute("data");

                        if (href is not null)
                        {
                            _elements.Add(element);
                            _tasks.Add(_report(null, _current, href, default));
                        }
                    }

                    break;
                case "meta":
                    {
                        var name = element.GetAttribute("name") ?? element.GetAttribute("property");
                        var content = element.GetAttribute("content");

                        if (name is not null && content is not null)
                        {
                            switch (name)
                            {
                                case "og:image":
                                case "og:audio":
                                case "og:video":
                                case "og:url":
                                    _elements.Add(element);
                                    _tasks.Add(_report(null, _current, content, default));
                                    break;

                            }
                        }
                    }

                    break;
            }
        }

        var nodes = await Task.WhenAll(_tasks);
        var replacements = GetReplacements(nodes, _elements);
        return new HtmlFragment(_current, document, replacements);
    }

    /// <summary>
    /// Reads an <c>&lt;img&gt;</c>'s <c>width</c>/<c>height</c> attributes as a
    /// requested image variant. Both, either, or neither may be set — when only
    /// one is given, the asset processor scales the other to match the source
    /// image's aspect ratio. Only plain unitless pixel integers are understood
    /// (as HTML width/height attributes are defined); anything else (a stray
    /// unit, a percentage, "auto") is ignored rather than guessed at.
    /// </summary>
    private static (int? Width, int? Height, string? Format) GetImageVariant(IElement element)
    {
        // No HTML attribute maps to an output format — that only comes from a
        // `?format=` query string on the src itself, parsed centrally in
        // Traverse.InnerProcess (e.g. `<img src="logo.png?format=webp">`).
        return (ParseDimension(element.GetAttribute("width")), ParseDimension(element.GetAttribute("height")), null);
    }

    private static int? ParseDimension(string? value)
    {
        return int.TryParse(value?.Trim(), out var pixels) && pixels > 0 ? pixels : null;
    }
}