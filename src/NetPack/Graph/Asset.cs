namespace NetPack.Graph;

using NetPack.Assets;

public sealed class Asset(Node root, string type, byte[] content, string hash = "")
{
    public Node Root => root;

    public string Type => type;

    public string Hash => hash;

    public byte[] Content => content;

    public string GetFileName()
    {
        var name = Path.GetFileNameWithoutExtension(Root.FileName);

        // A `?format=` variant changes the output extension (e.g. importing a
        // .png with `?format=webp` emits a .webp file); everything else keeps
        // the source file's own extension, same as before variants existed.
        var ext = Root.VariantFormat is not null ? $".{Root.VariantFormat}" : Path.GetExtension(Root.FileName);

        if (!string.IsNullOrEmpty(Hash))
        {
            return $"{name}.{Hash}{ext}";
        }

        return $"{name}{ext}";
    }

    public async Task<Stream> CreateStream(OutputOptions options)
    {
        var processor = AssetProcessorFactory.GetProcessor(Type);
        var src = await processor.ProcessAsync(this, options);
        return src;
    }
}
