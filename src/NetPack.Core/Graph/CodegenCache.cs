namespace NetPack.Graph;

using System.Collections.Concurrent;
using NetPack.Syntax.Ast;

/// <summary>
/// Phase 2 incremental cache: stores the lowered module body (post-Visit,
/// post-JSX-lowering, post-import-rewriting) keyed by file path and content
/// hash, so unchanged modules skip the expensive <c>JsxToJavaScriptTranspiler</c>
/// traversal during warm rebuilds.
///
/// Requires a stable <see cref="ModuleIdMap"/> across rebuilds — module ids are
/// baked into the cached bodies as <c>require(id)</c> calls. When shared across
/// build calls, pass the same <c>ModuleIdMap</c>.
/// </summary>
public class CodegenCache
{
    private readonly ConcurrentDictionary<string, CodegenEntry> _entries = [];

    /// <summary>
    /// Number of cache hits since the cache was created or reset.
    /// </summary>
    public int Hits { get; private set; }

    /// <summary>
    /// Number of cache misses (modules whose codegen output was not cached or
    /// whose content has changed).
    /// </summary>
    public int Misses { get; private set; }

    /// <summary>
    /// Number of stored entries.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Attempts to retrieve the cached lowered body for a module. Returns null
    /// when the module is not cached or its content hash has changed (stale).
    /// </summary>
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

    /// <summary>
    /// Stores or updates the lowered body for a module.
    /// </summary>
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

    /// <summary>
    /// Resets the hit/miss counters (not the stored entries).
    /// </summary>
    public void ResetCounters()
    {
        Hits = 0;
        Misses = 0;
    }
}

/// <summary>
/// A single cached codegen entry: the content hash used for invalidation,
/// the lowered module body statements ready for insertion into a factory,
/// and metadata for Fast Refresh / JSX auto-import.
/// </summary>
public class CodegenEntry
{
    public string ContentHash { get; init; } = "";
    public List<Statement> Body { get; init; } = [];
    public bool UsesJsx { get; init; }
    public string? FactorySource { get; init; }
}
