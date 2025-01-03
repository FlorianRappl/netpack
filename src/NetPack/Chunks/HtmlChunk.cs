namespace NetPack.Chunks;

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using NetPack.Graph;

class HtmlChunk(IDocument document, IDictionary<IElement, Graph.Node> replacements) : IChunk
{
    private readonly IDocument _document = document;
    private readonly IDictionary<IElement, Graph.Node> _replacements = replacements;

    public string Stringify(BundlerContext context, bool optimize)
    {
        foreach (var replacement in _replacements)
        {
            var element = replacement.Key;
            var node = replacement.Value;
            var bundle = context.Bundles.FirstOrDefault(m => m.Root == node);
            var asset = context.Assets.FirstOrDefault(m => m.Root == node);
            var reference = bundle?.GetFileName() ?? asset?.GetFileName() ?? Path.GetFileName(node.FileName);

            switch (element.LocalName)
            {
                case "link":
                case "a":
                    element.SetAttribute("href", reference);
                    break;
                case "script":
                case "img":
                case "video":
                case "audio":
                    element.SetAttribute("src", reference);
                    break;
            }
        }

        if (optimize)
        {
            foreach (var node in _document.Head!.ChildNodes.OfType<IText>().ToArray())
            {
                _document.Head.RemoveChild(node);
            }

            foreach (var node in _document.DocumentElement.ChildNodes.OfType<IText>().ToArray())
            {
                _document.DocumentElement.RemoveChild(node);
            }

            if (_document.Body!.ChildNodes.LastOrDefault() is IText text && string.IsNullOrWhiteSpace(text.Data))
            {
                _document.Body.RemoveChild(text);
            }
        }

        var formatter = optimize ? MinifyMarkupFormatter.Instance : HtmlMarkupFormatter.Instance;
        return _document.ToHtml(formatter);
    }
}
