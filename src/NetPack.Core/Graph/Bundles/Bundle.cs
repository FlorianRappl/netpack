namespace NetPack.Graph.Bundles;

public abstract class Bundle(BundlerContext context, Node root, BundleFlags flags)
{
    protected readonly BundlerContext _context = context;

    public Node Root => root;

    public bool IsPrimary => flags.HasFlag(BundleFlags.Primary);

    public bool IsShared => flags.HasFlag(BundleFlags.Shared);

    public string Name => root.FileName;

    public string Type => root.Type;

    /// <summary>
    /// The final output file name once a hashed naming template has been applied
    /// (see <see cref="AssignOutputName"/>). Null until assigned, in which case
    /// <see cref="GetFileName"/> derives the plain name from the entry.
    /// </summary>
    public string? OutputName { get; private set; }

    public Node[] Items = [];

    /// <summary>The source map produced by the last <see cref="CreateStream"/>
    /// (when source maps are enabled), to be written alongside the bundle.</summary>
    public byte[]? SourceMap { get; protected set; }

    /// <summary>The output name stem (no extension) — the dependency's package
    /// name for a shared library bundle, otherwise the entry file's own name.</summary>
    public string BaseName
    {
        get
        {
            var entry = Name;
            var dependency = _context.Dependencies.FirstOrDefault(m => m.Entry == entry);
            return dependency is not null
                ? Helpers.ToFileName(dependency.Name)
                : Path.GetFileNameWithoutExtension(entry);
        }
    }

    public string GetFileName() => OutputName ?? $"{BaseName}{Type}";

    /// <summary>
    /// Applies a naming template (<c>[name]</c>/<c>[hash]</c>) to fix this
    /// bundle's <see cref="OutputName"/>, e.g. <c>[name]-[hash]</c> →
    /// <c>app-1a2b3c.js</c>. Assigned before rendering so references resolve to
    /// the hashed name.
    /// </summary>
    public void AssignOutputName(string template, string hash)
    {
        var stem = template.Replace("[name]", BaseName).Replace("[hash]", hash);
        OutputName = $"{stem}{Type}";
    }

    public abstract Task<Stream> CreateStream(OutputOptions options);

    /// <summary>
    /// When the asset is small enough to inline (considering both the global
    /// <paramref name="options"/>.<see cref="OutputOptions.InlineLimit"/> and any
    /// per-import <c>?inline=</c> override stored on the node), returns its data
    /// URI; otherwise null. A node with <c>InlineLimitOverride == 0</c> is always
    /// inlined; <c>-1</c> is never inlined; a positive value overrides the
    /// threshold for this specific asset; <c>null</c> falls back to the global
    /// limit.
    /// </summary>
    protected string? TryGetInlineDataUri(Node node, OutputOptions options)
    {
        // Per-import `?inline=never` — force external, regardless of size.
        if (node.InlineLimitOverride == -1)
        {
            return null;
        }

        // Per-import `?inline=always` — force inline, regardless of size.
        if (node.InlineLimitOverride == 0)
        {
            if (_context.Assets.TryGetValue(node, out var asset))
            {
                return Helpers.ToDataUri(node.Extension, asset.Content);
            }

            return null;
        }

        // Per-import numeric — override the threshold for this asset.
        var limit = node.InlineLimitOverride ?? options.InlineLimit;

        if (limit > 0
            && _context.Assets.TryGetValue(node, out var assetNode)
            && assetNode.Content.Length <= limit)
        {
            return Helpers.ToDataUri(node.Extension, assetNode.Content);
        }

        return null;
    }

    protected string GetReference(Node node)
    {
        if (_context.Bundles.TryGetValue(node, out var bundle))
        {
            return bundle.GetFileName();
        }
        else if (_context.Assets.TryGetValue(node, out var asset))
        {
            return asset.GetFileName();
        }
        else
        {
            return Path.GetFileName(node.FileName);
        }
    }
}
