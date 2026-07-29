namespace NetPack.Graph;

using System.Collections.Concurrent;

public class Node(string fileName, int bytes, int? variantWidth = null, int? variantHeight = null, string? variantFormat = null, int? inlineLimitOverride = null)
{
    public string FileName => fileName;

    public string ParentDir => Path.GetDirectoryName(fileName)!;

    public string Extension => Path.GetExtension(fileName)!;

    public string Type => Helpers.GetType(Extension);

    public int Bytes => bytes;

    public bool IsEmpty => bytes == 0;

    public bool IsAsset => Helpers.IsAssetType(Type);

    /// <summary>
    /// The requested pixel width for an on-the-fly image variant (an
    /// <c>&lt;img width&gt;</c> attribute, a CSS <c>background-size</c>, or a
    /// <c>?width=</c> import query param). Null for a plain reference to the
    /// original file. When only one of <see cref="VariantWidth"/> /
    /// <see cref="VariantHeight"/> is set, the other is derived by scaling
    /// from the source image's own aspect ratio at asset-processing time.
    /// </summary>
    public int? VariantWidth => variantWidth;

    /// <summary>The requested pixel height for an on-the-fly image variant. See
    /// <see cref="VariantWidth"/>.</summary>
    public int? VariantHeight => variantHeight;

    /// <summary>
    /// The requested output format for an on-the-fly image variant, from a
    /// <c>?format=</c> reference/import query param (e.g.
    /// <c>./logo.png?format=webp</c>) — lowercase, no leading dot (<c>"webp"</c>,
    /// <c>"png"</c>, <c>"jpg"</c>, <c>"jpeg"</c>, <c>"gif"</c>, <c>"bmp"</c>).
    /// Null keeps the source file's own format.
    /// </summary>
    public string? VariantFormat => variantFormat;

    /// <summary>True when this node is a resized and/or re-encoded variant of
    /// another file's content rather than a plain reference to it.</summary>
    public bool IsVariant => variantWidth is not null || variantHeight is not null || variantFormat is not null;

    /// <summary>
    /// Per-import inline override from a <c>?inline=</c> query parameter.
    /// <c>null</c> means no override (use the global threshold).
    /// <c>0</c> (<c>?inline=always</c>) forces inlining regardless of size.
    /// <c>-1</c> (<c>?inline=never</c>) prevents inlining regardless of size.
    /// A positive value overrides the threshold in bytes for this asset
    /// (<c>?inline=100</c> → 100&#x202F;KB → 102400).
    /// </summary>
    public int? InlineLimitOverride => inlineLimitOverride;

    public ConcurrentBag<Node> Children = [];

    public ConcurrentBag<Node> References = [];
}
