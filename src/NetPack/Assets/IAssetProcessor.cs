namespace NetPack.Assets;

using NetPack.Graph;

/// <summary>
/// Turns an emitted <see cref="Asset"/> into its output stream. The core library
/// ships only a pass-through implementation; a consumer registers richer
/// processors (image resizing/re-encoding, etc.) with
/// <see cref="AssetProcessorFactory.Register"/>. netpack's own CLI provides a
/// SkiaSharp-based image processor this way, keeping the core dependency-free.
/// </summary>
public interface IAssetProcessor
{
    Task<Stream> ProcessAsync(Asset asset, OutputOptions options);
}
