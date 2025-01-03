namespace NetPack.Assets;

static class AssetProcessorFactory
{
    private static readonly ImageAssetProcessor imageAssetProcessor = new();
    private static readonly DefaultAssetProcessor defaultAssetProcessor = new();

    private static readonly Dictionary<string, IAssetProcessor> _processors = new()
    {
        { ".png", imageAssetProcessor },
        { ".jpg", imageAssetProcessor },
        { ".jpeg", imageAssetProcessor },
        { ".webp", imageAssetProcessor },
        { ".gif", imageAssetProcessor },
        { ".bmp", imageAssetProcessor },
        { ".exif", imageAssetProcessor },
    };

    public static IAssetProcessor GetProcessor(string type)
    {
        if (_processors.TryGetValue(type, out var processor))
        {
            return processor;
        }

        return defaultAssetProcessor;
    }
}
