namespace NetPack.Assets;

using NetPack.Graph;

class DefaultAssetProcessor : IAssetProcessor
{
    public Task<Stream> ProcessAsync(Asset asset, bool optimize)
    {
        Stream fs = File.OpenRead(asset.Root.FileName);
        return Task.FromResult(fs);
    }
}
