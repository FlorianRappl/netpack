namespace NetPack.Assets;

using NetPack.Graph;

class HtmlAssetProcessor : IAssetProcessor
{
    public Task<Stream> ProcessAsync(Asset asset, bool optimize)
    {
        Stream fs = File.OpenRead(asset.Root.FileName);
        return Task.FromResult(fs);
    }
}
