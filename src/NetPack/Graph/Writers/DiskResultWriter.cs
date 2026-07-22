namespace NetPack.Graph.Writers;

using System.Linq;
using NetPack.Server;

sealed class DiskResultWriter(BundlerContext context, string target) : ResultWriter(context), IFileLocator
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

    /// <summary>True when <paramref name="fullPath"/> is a source file that took
    /// part in this build — used by watch mode to decide whether a filesystem
    /// change warrants a rebuild.</summary>
    public bool HasFile(string fullPath) => _context.Modules.Values.Any(m => m.FileName == fullPath);
}
