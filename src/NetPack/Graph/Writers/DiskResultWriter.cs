namespace NetPack.Graph.Writers;

sealed class DiskResultWriter(BundlerContext context, string target) : ResultWriter(context)
{
    private readonly string _target = target;

    protected override Stream OpenWrite(string name)
    {
        var fileName = Path.Combine(_target, name);
        return File.OpenWrite(fileName);
    }
}
