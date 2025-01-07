namespace NetPack.Graph.Bundles;

using System.Collections.Concurrent;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html;
using NetPack.Fragments;

public sealed class HtmlBundle(BundlerContext context, Graph.Node root, BundleFlags flags) : Bundle(root, flags)
{
    private static readonly ConcurrentBag<HtmlFragment> _fragments = [];

    public static ConcurrentBag<HtmlFragment> Fragments => _fragments;

    private readonly BundlerContext _context = context;

    public override Task<Stream> CreateStream(OutputOptions options)
    {
        var src = new MemoryStream();
        Stringify(src, options);
        src.Position = 0;
        return Task.FromResult<Stream>(src);
    }

    private void Stringify(MemoryStream ms, OutputOptions options)
    {
        var root = _fragments.FirstOrDefault(m => m.Root == Root);

        if (root is not null)
        {
            var replacements = root.Replacements;
            var document = root.Document;

            foreach (var replacement in replacements)
            {
                var element = replacement.Key;
                var node = replacement.Value;
                var bundle = _context.Bundles.FirstOrDefault(m => m.Root == node);
                var asset = _context.Assets.FirstOrDefault(m => m.Root == node);
                var reference = bundle?.GetFileName() ?? asset?.GetFileName() ?? Path.GetFileName(node.FileName);

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
                        element.SetAttribute("src", $"./{reference}");
                        break;
                }
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
}
