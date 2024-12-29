namespace NetPack.Graph;

public class Node(string fileName)
{
    public string FileName => fileName;

    public string ParentDir => Path.GetDirectoryName(fileName)!;

    public string Extension => Path.GetExtension(fileName)!;

    public string Type => Helpers.GetType(Extension);

    public List<Node> Children = [];

    public List<Node> References = [];
}
