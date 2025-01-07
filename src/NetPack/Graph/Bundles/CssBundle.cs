namespace NetPack.Graph.Bundles;

using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Css;
using NetPack.Fragments;

public sealed class CssBundle(BundlerContext context, Node root, BundleFlags flags) : Bundle(root, flags)
{
    private static readonly ConcurrentBag<CssFragment> _fragments = [];

    public static ConcurrentBag<CssFragment> Fragments => _fragments;

    private readonly BundlerContext _context = context;

    public override Task<Stream> CreateStream(bool optimize)
    {
        var src = new MemoryStream();
        Stringify(src, optimize);
        src.Position = 0;
        return Task.FromResult<Stream>(src);
    }
    
    private void Stringify(MemoryStream ms, bool optimize)
    {
        var root = _fragments.FirstOrDefault(m => m.Root == Root);

        if (root is not null)
        {
            var replacements = root.Replacements;
            var stylesheet = root.Stylesheet;

            foreach (var replacement in replacements)
            {
                var property = replacement.Key;
                var node = replacement.Value;
                var bundle = _context.Bundles.FirstOrDefault(m => m.Root == node);
                var asset = _context.Assets.FirstOrDefault(m => m.Root == node);
                var reference = bundle?.GetFileName() ?? asset?.GetFileName() ?? Path.GetFileName(node.FileName);
                property.Value = Regex.Replace(property.Value, @"url\(.*\)", $"url('./{reference}')");
            }

            var formatter = optimize ? new MinifyStyleFormatter() : CssStyleFormatter.Instance;
            using var writer = new StreamWriter(ms, Encoding.UTF8, -1, true);
            stylesheet.ToCss(writer, formatter);
        }
    }
}
