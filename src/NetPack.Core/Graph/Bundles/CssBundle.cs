namespace NetPack.Graph.Bundles;

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Css;

public sealed class CssBundle(BundlerContext context, Node root, BundleFlags flags) : Bundle(context, root, flags)
{
    /// <summary>
    /// When set, this bundle is a per-chunk combined stylesheet: the listed CSS
    /// nodes are rendered concatenated in order (each with its own <c>url()</c>
    /// asset rewrites) rather than the single <see cref="Bundle.Root"/> fragment.
    /// Used by the <c>--css link</c>/<c>none</c> build-time split, which merges all
    /// of a JS chunk's non-shared stylesheets into one file.
    /// </summary>
    public List<Node> Parts { get; } = [];

    public override Task<Stream> CreateStream(OutputOptions options)
    {
        // A combined bundle renders from several source fragments whose content is
        // not captured by the render-cache key (which keys on this bundle's own
        // Items), so skip the cache for it to stay correct across rebuilds.
        if (Parts.Count == 0 && TryGetRenderCache(options) is { } cached)
        {
            return Task.FromResult<Stream>(new MemoryStream(cached));
        }

        var src = new MemoryStream();
        Stringify(src, options);
        src.Position = 0;

        if (Parts.Count == 0)
        {
            PutRenderCache(options, src.ToArray());
            src.Position = 0;
        }

        return Task.FromResult<Stream>(src);
    }

    private void Stringify(MemoryStream ms, OutputOptions options)
    {
        var sources = Parts.Count > 0 ? (IReadOnlyList<Node>)Parts : [Root];

        foreach (var source in sources)
        {
            if (!_context.CssFragments.TryGetValue(source, out var fragment))
            {
                continue;
            }

            var replacements = fragment.Replacements;
            var stylesheet = fragment.Stylesheet;

            foreach (var replacement in replacements)
            {
                var property = replacement.Key;
                var node = replacement.Value;
                var url = TryGetInlineDataUri(node, options) ?? Helpers.PublicUrl(options.PublicPath, GetReference(node));
                property.Value = Regex.Replace(property.Value, @"url\(.*\)", $"url('{url}')");
            }

            var formatter = options.IsOptimizing ? new MinifyStyleFormatter() : CssStyleFormatter.Instance;
            using var writer = new StreamWriter(ms, Encoding.UTF8, -1, true);
            stylesheet.ToCss(writer, formatter);
        }
    }
}
