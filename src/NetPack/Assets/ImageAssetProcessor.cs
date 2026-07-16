namespace NetPack.Assets;

using NetPack.Graph;
using SkiaSharp;

class ImageAssetProcessor : IAssetProcessor
{
    public Task<Stream> ProcessAsync(Asset asset, OutputOptions options)
    {
        var requestedWidth = asset.Root.VariantWidth;
        var requestedHeight = asset.Root.VariantHeight;
        var requestedFormat = asset.Root.VariantFormat;
        var isVariant = requestedWidth is not null || requestedHeight is not null || requestedFormat is not null;

        // Decoding/re-encoding is only worth the cost when there's something to
        // change: either a real, on-the-fly resize/format switch was requested
        // (regardless of --minify), or a full optimizing build wants the
        // quality-90 re-encode. Otherwise, hand back the original bytes
        // untouched.
        if (!options.IsOptimizing && !isVariant)
        {
            return Task.FromResult<Stream>(File.OpenRead(asset.Root.FileName));
        }

        using var fs = File.OpenRead(asset.Root.FileName);
        using var skData = SKData.Create(fs);
        using var codec = SKCodec.Create(skData);
        using var sourceImage = SKBitmap.Decode(codec);
        var (targetWidth, targetHeight) = ResolveVariantSize(sourceImage.Width, sourceImage.Height, requestedWidth, requestedHeight);
        var info = new SKImageInfo(targetWidth, targetHeight);
        using var resizedImage = sourceImage.Resize(info, SKSamplingOptions.Default);
        var outputFormat = ResolveOutputFormat(requestedFormat, asset.Type);
        Stream output = new MemoryStream();
        using var outputImage = SKImage.FromBitmap(resizedImage ?? sourceImage);
        using var data = outputImage.Encode(outputFormat, 90);
        data.SaveTo(output);
        output.Position = 0;
        return Task.FromResult(output);
    }

    /// <summary>
    /// Picks the SkiaSharp encoder format: a requested <c>?format=</c> override
    /// wins (falling back to the source's own format for an unrecognized
    /// value, which shouldn't happen — <c>Traverse.ParseVariantQuery</c> already
    /// filters to the formats listed below); otherwise the source file's own
    /// extension decides, same as before variants existed.
    /// </summary>
    private static SKEncodedImageFormat ResolveOutputFormat(string? requestedFormat, string sourceType)
    {
        return requestedFormat switch
        {
            "png" => SKEncodedImageFormat.Png,
            "webp" => SKEncodedImageFormat.Webp,
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "gif" => SKEncodedImageFormat.Gif,
            "bmp" => SKEncodedImageFormat.Bmp,
            _ => sourceType switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                ".avif" => SKEncodedImageFormat.Avif,
                ".jpg" => SKEncodedImageFormat.Jpeg,
                ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".ico" => SKEncodedImageFormat.Ico,
                ".gif" => SKEncodedImageFormat.Gif,
                ".bmp" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Webp,
            },
        };
    }

    /// <summary>
    /// Resolves the pixel size to resize to. Neither dimension requested means
    /// "no variant" (the plain optimizing re-encode, same size as the source).
    /// Both requested is used as-is. Only one requested scales the other from
    /// the source image's own aspect ratio — an <c>&lt;img width="200"&gt;</c>
    /// with no <c>height</c>, or vice versa, "just scales", per how the browser
    /// itself treats a single sizing attribute.
    /// </summary>
    private static (int Width, int Height) ResolveVariantSize(int sourceWidth, int sourceHeight, int? requestedWidth, int? requestedHeight)
    {
        if (requestedWidth is null && requestedHeight is null)
        {
            return (sourceWidth, sourceHeight);
        }

        if (requestedWidth is not null && requestedHeight is not null)
        {
            return (requestedWidth.Value, requestedHeight.Value);
        }

        if (requestedWidth is not null)
        {
            var scaledHeight = (int)Math.Round(sourceHeight * (requestedWidth.Value / (double)sourceWidth));
            return (requestedWidth.Value, Math.Max(1, scaledHeight));
        }

        var scaledWidth = (int)Math.Round(sourceWidth * (requestedHeight!.Value / (double)sourceHeight));
        return (Math.Max(1, scaledWidth), requestedHeight.Value);
    }
}
