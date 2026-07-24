namespace NetPack.Graph.Writers;

using NetPack.Server;

sealed class MemoryResultWriter(BundlerContext context) : ResultWriter(context), IFileLocator
{
    private readonly Dictionary<string, byte[]> _fs = [];

    public byte[]? GetFile(string name)
    {
        if (_fs.TryGetValue(name, out var content))
        {
            return content;
        }
        
        return null;
    }

    bool IFileLocator.HasFile(string fullPath)
    {
        return _context.Modules.Values.Any(m => m.FileName == fullPath);
    }

    protected override Stream OpenWrite(string name)
    {
        return new MemoryStream();
    }

    protected override void CloseWrite(string name, Stream stream)
    {
        if (stream is MemoryStream ms)
        {
            _fs[name] = ms.ToArray();
        }
    }
}
