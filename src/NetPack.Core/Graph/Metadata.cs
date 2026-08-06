namespace NetPack.Graph;

using System.Text.Json;
using NetPack.Graph.Writers;
using NetPack.Json;
using NetPack.Server;

class Metadata(Traverse graph, MemoryResultWriter compilation, AuditReport? audit = null, int inlineLimit = 0) : IFileLocator
{
    private readonly Traverse _graph = graph;
    private readonly MemoryResultWriter _compilation = compilation;
    private readonly AuditReport? _audit = audit;
    private readonly int _inlineLimit = inlineLimit;

    bool IFileLocator.HasFile(string fullPath)
    {
        return ((IFileLocator)_compilation).HasFile(fullPath);
    }

    bool IFileLocator.HasDirectory(string fullPath)
    {
        return ((IFileLocator)_compilation).HasDirectory(fullPath);
    }

    public string Stringify()
    {
        var context = _graph.Context;
        var emitted = _compilation.GetFileNames().Select(name =>
            new EmittedFile(name, _compilation.GetFile(name)?.Length ?? 0, Modules: 0, IsBundle: false)).ToList();

        return Traverse.BuildMetafile(context, emitted, _audit, _inlineLimit);
    }
}
