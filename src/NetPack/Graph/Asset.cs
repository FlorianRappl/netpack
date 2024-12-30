namespace NetPack.Graph;

public sealed class Asset(Node root, string type)
{
    public Node Root => root;

    public string Type => type;
}
