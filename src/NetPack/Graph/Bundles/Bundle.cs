namespace NetPack.Graph.Bundles;

public abstract class Bundle(BundlerContext context, Node root, BundleFlags flags)
{
    protected readonly BundlerContext _context = context;

    public Node Root => root;

    public bool IsPrimary => flags.HasFlag(BundleFlags.Primary);

    public bool IsShared => flags.HasFlag(BundleFlags.Shared);

    public string Name => root.FileName;

    public string Type => root.Type;

    public Node[] Items = [];

    public string GetFileName()
    {
        var entry = Name;
        var dependency = _context.Dependencies.FirstOrDefault(m => m.Entry == entry);

        if (dependency is not null)
        {
            var name = Helpers.ToFileName(dependency.Name);
            return $"{name}{Type}";
        }
        else
        {
            var name = Path.GetFileNameWithoutExtension(entry);
            return $"{name}{Type}";
        }
    }

    public abstract Task<Stream> CreateStream(OutputOptions options);

    protected string GetReference(Node node)
    {
        if (_context.Bundles.TryGetValue(node, out var bundle))
        {
            return bundle.GetFileName();
        }
        else if (_context.Assets.TryGetValue(node, out var asset))
        {
            return asset.GetFileName();
        }
        else
        {
            return Path.GetFileName(node.FileName);
        }
    }
}
