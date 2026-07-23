namespace NetPack.Assets;

/// <summary>
/// Registry of per-extension <see cref="IAssetProcessor"/>s. The core library
/// registers nothing native — unknown asset types fall back to a pass-through
/// processor that copies the file verbatim. Consumers (including netpack's own
/// CLI, which registers a SkiaSharp image processor) opt into richer handling via
/// <see cref="Register"/>, so the core stays free of native dependencies.
/// </summary>
public static class AssetProcessorFactory
{
    private static readonly DefaultAssetProcessor _default = new();
    private static readonly Dictionary<string, IAssetProcessor> _processors = new();

    /// <summary>Registers <paramref name="processor"/> for a file extension
    /// (including the leading dot, e.g. <c>".png"</c>). Later registrations for the
    /// same extension win. Configure processors before bundling.</summary>
    public static void Register(string extension, IAssetProcessor processor)
        => _processors[extension] = processor;

    /// <summary>The processor for an asset type, or the pass-through default.</summary>
    public static IAssetProcessor GetProcessor(string type)
        => _processors.TryGetValue(type, out var processor) ? processor : _default;
}
