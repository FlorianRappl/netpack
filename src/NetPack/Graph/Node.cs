namespace NetPack.Graph;

using System.Collections.Concurrent;

public class Node(string fileName)
{
    public string FileName => fileName;

    public string ParentDir => Path.GetDirectoryName(fileName)!;

    public string Extension => Path.GetExtension(fileName)!;

    public string Type => Helpers.GetType(Extension);

    public ConcurrentBag<Node> Children = [];

    public ConcurrentBag<Node> References = [];
}
