namespace NetPack.Graph.Bundles;

using System.Text;

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

    /// <summary>True when an asset should be inlined as a data URI rather than
    /// emitted as a file. Respects the global <c>--inline-limit</c> and any
    /// per-import <c>?inline=</c> override on the node.</summary>
    public static bool IsInlined(Node node, Asset asset, OutputOptions options)
    {
        if (node.InlineLimitOverride == -1) return false;
        if (node.InlineLimitOverride > 0)
            return asset.Content.Length <= node.InlineLimitOverride.Value;

        return options.InlineLimit > 0 && asset.Content.Length <= options.InlineLimit;
    }

    protected string? TryGetInlineDataUri(Node node, OutputOptions options)
    {
        if (_context.Assets.TryGetValue(node, out var asset) && IsInlined(node, asset, options))
        {
            return Helpers.ToDataUri(node.Extension, asset.Content);
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

    // -- Phase 3 render cache helpers ---------------------------------------

    /// <summary>
    /// Computes a stable key for this bundle's render cache entry: the bundle
    /// name + output config + all module content hashes in the bundle. When
    /// no module content has changed, the same key is produced and the cached
    /// bytes can be reused.
    /// </summary>
    protected string ComputeRenderKey(OutputOptions options)
    {
        var sb = new StringBuilder();
        sb.Append(Name);
        sb.Append('|');
        sb.Append(options.Format);
        sb.Append('|');
        sb.Append(options.PublicPath ?? "");
        sb.Append('|');
        sb.Append(options.Banner ?? "");
        sb.Append('|');
        sb.Append(options.IsOptimizing ? '1' : '0');
        sb.Append('|');
        sb.Append(options.IsReloading ? '1' : '0');
        sb.Append('|');
        sb.Append(options.WithSourceMaps ? '1' : '0');

        foreach (var node in Items.OrderBy(n => n.FileName, StringComparer.Ordinal))
        {
            sb.Append('|');
            // Use the content hash stored on the node (set during Traverse).
            // Falls back to file name when no hash is available (CSS/HTML).
            sb.Append(node.ContentHash ?? node.FileName);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Checks the Phase 3 render cache for this bundle's rendered bytes.
    /// Returns the cached bytes on hit, null on miss.
    /// </summary>
    protected byte[]? TryGetRenderCache(OutputOptions options)
    {
        var cache = _context.RenderCache;
        if (cache is null)
        {
            return null;
        }

        var key = ComputeRenderKey(options);
        return cache.Get(key);
    }

    /// <summary>
    /// Stores this bundle's rendered bytes in the Phase 3 render cache.
    /// </summary>
    protected void PutRenderCache(OutputOptions options, byte[] bytes)
    {
        var cache = _context.RenderCache;
        if (cache is null)
        {
            return;
        }

        var key = ComputeRenderKey(options);
        cache.Put(key, bytes);
    }
}
