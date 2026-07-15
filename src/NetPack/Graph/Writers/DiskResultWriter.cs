namespace NetPack.Graph.Writers;

sealed class DiskResultWriter(BundlerContext context, string target) : ResultWriter(context)
{
    private readonly string _target = target;

    protected override Stream OpenWrite(string name)
    {
        var fileName = Path.Combine(_target, name);
        // File.Create truncates an existing file to zero length first. File.OpenWrite
        // (FileMode.OpenOrCreate) would leave any trailing bytes from a previous,
        // longer build in place — corrupting a bundle whenever a rebuild produces a
        // shorter file (e.g. after minification or tree-shaking removes code).
        return File.Create(fileName);
    }
}
