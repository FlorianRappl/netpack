namespace NetPack.Chunks;

using NetPack.Graph;

public interface IChunk
{
    string Stringify(BundlerContext context, bool optimize);
}
