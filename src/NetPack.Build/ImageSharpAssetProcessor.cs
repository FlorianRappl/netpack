namespace NetPack.Build;

using System.IO;
using System.Threading.Tasks;
using NetPack.Assets;
using NetPack.Graph;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

/// <summary>
/// A pure-managed, cross-platform <see cref="IAssetProcessor"/> for images —
/// resize and re-encode via <c>SixLabors.ImageSharp</c>, with no native/OS
/// dependency (the equivalent of the native CLI's SkiaSharp processor). Formats
/// it doesn't handle (e.g. <c>.avif</c>, <c>.ico</c>) are simply not registered,
/// so they fall through to the core's pass-through default.
/// </summary>
public sealed class ImageSharpAssetProcessor : IAssetProcessor
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    /// <summary>Registers this processor for the image extensions it supports.</summary>
    public static void Register()
    {
        var processor = new ImageSharpAssetProcessor();

        foreach (var extension in Extensions)
        {
            AssetProcessorFactory.Register(extension, processor);
        }
    }

    public async Task<Stream> ProcessAsync(Asset asset, OutputOptions options)
    {
        var width = asset.Root.VariantWidth;
        var height = asset.Root.VariantHeight;
        var format = asset.Root.VariantFormat;
        var isVariant = width is not null || height is not null || format is not null;

        // Only decode/re-encode when there is something to change: a real resize /
        // format switch, or an optimizing build's re-encode. Otherwise pass the
        // original bytes through untouched.
        if (!options.IsOptimizing && !isVariant)
        {
            return File.OpenRead(asset.Root.FileName);
        }

        using var image = await Image.LoadAsync(asset.Root.FileName).ConfigureAwait(false);

        if (width is not null || height is not null)
        {
            // A zero dimension tells ImageSharp to derive it from the source aspect
            // ratio — matching a single width/height sizing attribute.
            image.Mutate(context => context.Resize(width ?? 0, height ?? 0));
        }

        var encoder = GetEncoder(format ?? asset.Type.TrimStart('.'));
        var output = new MemoryStream();
        await image.SaveAsync(output, encoder).ConfigureAwait(false);
        output.Position = 0;
        return output;
    }

    private static IImageEncoder GetEncoder(string format) => format.ToLowerInvariant() switch
    {
        "png" => new PngEncoder(),
        "webp" => new WebpEncoder(),
        "gif" => new GifEncoder(),
        "bmp" => new BmpEncoder(),
        "jpg" or "jpeg" => new JpegEncoder { Quality = 90 },
        _ => new PngEncoder(),
    };
}
