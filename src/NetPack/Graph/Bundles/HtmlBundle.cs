namespace NetPack.Graph.Bundles;

using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html;

public sealed class HtmlBundle(BundlerContext context, Graph.Node root, BundleFlags flags) : Bundle(context, root, flags)
{
    public override Task<Stream> CreateStream(OutputOptions options)
    {
        var src = new MemoryStream();
        Stringify(src, options);
        src.Position = 0;
        return Task.FromResult<Stream>(src);
    }

    private void Stringify(MemoryStream ms, OutputOptions options)
    {
        var fragments = _context.HtmlFragments;

        if (fragments.TryGetValue(Root, out var root))
        {
            var replacements = root.Replacements;
            var document = root.Document;

            foreach (var replacement in replacements)
            {
                var element = replacement.Key;
                var node = replacement.Value;
                var reference = GetReference(node);

                switch (element.LocalName)
                {
                    case "link":
                    case "a":
                        element.SetAttribute("href", $"./{reference}");
                        break;
                    case "script":
                        element.SetAttribute("type", "module");
                        element.SetAttribute("src", $"./{reference}");
                        break;
                    case "img":
                    case "video":
                    case "audio":
                    case "source":
                    case "iframe":
                        element.SetAttribute("src", $"./{reference}");
                        break;
                    case "object":
                        element.SetAttribute("data", $"./{reference}");
                        break;
                    case "meta":
                        element.SetAttribute("content", $"./{reference}");
                        break;
                }
            }

            if (_context.Shared.Count > 0)
            {
                var importmap = document.QuerySelector("script[type=importmap]");

                if (importmap is null)
                {
                    importmap = document.CreateElement("script");
                    importmap.SetAttribute("type", "importmap");
                    document.Head!.AppendChild(importmap);
                }

                var content = ReadImportmap(importmap);

                foreach (var dependency in _context.Shared)
                {
                    var name = Helpers.ToFileName(dependency);
                    content.Imports!.Add(dependency, $"./{name}.js");
                }

                WriteImportmap(importmap, content);
            }

            if (options.IsOptimizing)
            {
                foreach (var node in document.Head!.ChildNodes.OfType<IText>().ToArray())
                {
                    document.Head.RemoveChild(node);
                }

                foreach (var node in document.DocumentElement.ChildNodes.OfType<IText>().ToArray())
                {
                    document.DocumentElement.RemoveChild(node);
                }

                if (document.Body!.ChildNodes.LastOrDefault() is IText text && string.IsNullOrWhiteSpace(text.Data))
                {
                    document.Body.RemoveChild(text);
                }
            }

            if (options.IsReloading)
            {
                var child = document.CreateElement("script");
                child.TextContent = "new EventSource('/netpack').addEventListener('change', () => location.reload())";
                document.Body?.AppendChild(child);
            }

            var formatter = options.IsOptimizing ? MinifyMarkupFormatter.Instance : HtmlMarkupFormatter.Instance;
            using var writer = new StreamWriter(ms, Encoding.UTF8, -1, true);
            document.ToHtml(writer, formatter);
        }
    }

    private static void WriteImportmap(IElement importmap, Importmap content)
    {
        var source = JsonSerializer.Serialize(content, SourceGenerationContext.Default.Importmap);
        importmap.TextContent = source;
    }

    private static Importmap ReadImportmap(IElement importmap)
    {
        var source = importmap.TextContent;
        
        try
        {
            var current = JsonSerializer.Deserialize(source, SourceGenerationContext.Default.Importmap);

            if (current?.Imports is not null)
            {
                return current;
            }
        }
        catch
        {
            // Ignore importmap issues
        }

        return new Importmap
        {
            Imports = [],
        };
    }
}
