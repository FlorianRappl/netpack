namespace NetPack.Assets;

using NetPack.Graph;
using SkiaSharp;

class ImageAssetProcessor : IAssetProcessor
{
    public Task<Stream> ProcessAsync(Asset asset, bool optimize)
    {
        Stream fs = File.OpenRead(asset.Root.FileName);

        if (optimize)
        {
            using var skData = SKData.Create(fs);
            using var codec = SKCodec.Create(skData);
            using var destinationImage = SKBitmap.Decode(codec);
            var info = new SKImageInfo(destinationImage.Width, destinationImage.Height);
            using var resizedImage = destinationImage.Resize(info, SKSamplingOptions.Default);
            var outputFormat = asset.Type switch
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
            };
            Stream output = new MemoryStream();
            using var outputImage = SKImage.FromBitmap(resizedImage);
            using var data = outputImage.Encode(outputFormat, 90);
            data.SaveTo(output);
            fs.Dispose();
            output.Position = 0;
            return Task.FromResult(output);
        }

        return Task.FromResult(fs);
    }
}
