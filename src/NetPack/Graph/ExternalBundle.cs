namespace NetPack.Graph;

using System.Text;

public sealed class ExternalBundle(BundlerContext context, Node root) : Bundle(root, BundleFlags.Shared)
{
    private readonly BundlerContext _context = context;

    public override async Task<Stream> CreateStream(bool optimize)
    {
        var raw = Encoding.UTF8.GetBytes($"export * from '{Root.FileName}';");
        var src = new MemoryStream();
        await src.WriteAsync(raw);
        src.Position = 0;
        return src;
    }
}
