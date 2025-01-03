namespace NetPack.Chunks;

using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using NetPack.Graph;

class CssChunk(ICssStyleSheet document, IDictionary<ICssProperty, Graph.Node> replacements) : IChunk
{
    private readonly ICssStyleSheet _document = document;
    private readonly IDictionary<ICssProperty, Graph.Node> _replacements = replacements;

    public string Stringify(BundlerContext context, bool optimize)
    {
        foreach (var replacement in _replacements)
        {
            var property = replacement.Key;
            var node = replacement.Value;
            var bundle = context.Bundles.FirstOrDefault(m => m.Root == node);
            var asset = context.Assets.FirstOrDefault(m => m.Root == node);
            var reference = bundle?.GetFileName() ?? asset?.GetFileName() ?? Path.GetFileName(node.FileName);
            property.Value = Regex.Replace(property.Value, @"url\(.*\)", $"url('{reference}')");
        }

        var formatter = optimize ? new MinifyStyleFormatter() : CssStyleFormatter.Instance;
        return _document.ToCss(formatter);
    }
}
