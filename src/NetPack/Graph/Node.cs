namespace NetPack.Graph;

using System.Collections.Concurrent;

public class Node(string fileName, int bytes)
{
    public string FileName => fileName;

    public string ParentDir => Path.GetDirectoryName(fileName)!;

    public string Extension => Path.GetExtension(fileName)!;

    public string Type => Helpers.GetType(Extension);

    public int Bytes => bytes;

    public bool IsEmpty => bytes == 0;

    public bool IsAsset => Helpers.IsAssetType(Type);

    public ConcurrentBag<Node> Children = [];

    public ConcurrentBag<Node> References = [];
}
