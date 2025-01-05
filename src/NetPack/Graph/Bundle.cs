namespace NetPack.Graph;

public abstract class Bundle(Node root, BundleFlags flags)
{
    public Node Root => root;

    public bool IsPrimary => flags.HasFlag(BundleFlags.Primary);

    public bool IsShared => flags.HasFlag(BundleFlags.Shared);

    public string Name => root.FileName;

    public string Type => root.Type;

    public List<Node> Items = [];

    public string GetFileName()
    {
        var name = Path.GetFileNameWithoutExtension(Root.FileName);
        return $"{name}{Type}";
    }

    public abstract Task<Stream> CreateStream(bool optimize);
}
