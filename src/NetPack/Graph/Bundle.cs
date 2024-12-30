namespace NetPack.Graph;

public sealed class Bundle(Node root)
{
    public Node Root => root;

    public string Type => root.Type;

    public List<Node> Items = [root];
}
