namespace NetPack.Graph;

using NetPack.Assets;

public sealed class Asset(Node root, string type, string hash = "")
{
    public Node Root => root;

    public string Type => type;

    public string Hash => hash;

    public string GetFileName()
    {
        if (!string.IsNullOrEmpty(Hash))
        {
            var name = Path.GetFileNameWithoutExtension(Root.FileName);
            var ext = Path.GetExtension(Root.FileName);
            return $"{name}.{Hash}{ext}";
        }

        return Path.GetFileName(Root.FileName);
    }

    public async Task<Stream> CreateStream(bool optimize)
    {
        var processor = AssetProcessorFactory.GetProcessor(Type);
        var src = await processor.ProcessAsync(this, optimize);
        return src;
    }
}
