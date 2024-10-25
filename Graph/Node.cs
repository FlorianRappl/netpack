namespace NewBundler.Graph;

public class Node(string name)
{
    public string Name => name;
    public List<Node> References { get; set; } = [];
}
