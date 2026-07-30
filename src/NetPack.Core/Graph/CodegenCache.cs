namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Syntax.Ast;

/// <summary>
/// Stores lowered module bodies (post-JSX-lowering, post-import-rewriting)
/// keyed by content hash, so unchanged modules skip Transpile traversal
/// during warm rebuilds. Requires a stable <see cref="ModuleIdMap"/> — module
/// ids are baked into the cached <c>require(id)</c> calls.
/// </summary>
public class CodegenCache
{
    private readonly ConcurrentDictionary<string, CodegenEntry> _entries = [];

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Count => _entries.Count;

    public CodegenEntry? Get(string filePath, string contentHash)
    {
        if (_entries.TryGetValue(filePath, out var entry) && entry.ContentHash == contentHash)
        {
            Hits++;
            return entry;
        }

        Misses++;
        return null;
    }

    public void Put(string filePath, string contentHash, List<Statement> body, bool usesJsx, string? factorySource)
    {
        _entries[filePath] = new CodegenEntry
        {
            ContentHash = contentHash,
            Body = body,
            UsesJsx = usesJsx,
            FactorySource = factorySource,
        };
    }

    public void ResetCounters()
    {
        Hits = 0;
        Misses = 0;
    }
}

public class CodegenEntry
{
    public string ContentHash { get; init; } = "";
    public List<Statement> Body { get; init; } = [];
    public bool UsesJsx { get; init; }
    public string? FactorySource { get; init; }
}
