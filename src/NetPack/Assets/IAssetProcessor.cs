namespace NetPack.Assets;

using NetPack.Graph;

interface IAssetProcessor
{
    Task<Stream> ProcessAsync(Asset asset, OutputOptions optimize);
}
