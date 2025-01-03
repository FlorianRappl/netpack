namespace NetPack.Graph;

public sealed class Bundle(Node root)
{
    public Node Root => root;

    public string Name => root.FileName;

    public string Type => root.Type;

    public List<Node> Items = [];

    public string GetFileName()
    {
        var name = Path.GetFileNameWithoutExtension(Root.FileName);
        return $"{name}{Type}";
    }
}
