namespace NetPack.Graph;

using System.Collections.Concurrent;

/// <summary>
/// Phase 3 incremental cache: stores the rendered output of a bundle (the final
/// bytes after Stringify/printing) keyed by a hash of all module content hashes
/// in the bundle plus the output config, so unchanged chunks skip the entire
/// render pipeline during warm rebuilds.
/// </summary>
public class RenderCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = [];

    /// <summary>Number of cache hits since the cache was created or reset.</summary>
    public int Hits { get; private set; }

    /// <summary>Number of cache misses.</summary>
    public int Misses { get; private set; }

    /// <summary>Number of stored entries.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Attempts to retrieve cached rendered bytes for a bundle. Returns null
    /// when the bundle is not cached. The key is the stable content hash
    /// computed by <c>Bundle.ComputeRenderKey</c>.
    /// </summary>
    public byte[]? Get(string key)
    {
        if (_entries.TryGetValue(key, out var bytes))
        {
            Hits++;
            return bytes;
        }

        Misses++;
        return null;
    }

    /// <summary>
    /// Stores the rendered bytes for a bundle, keyed by the stable content hash.
    /// </summary>
    public void Put(string key, byte[] bytes)
    {
        _entries[key] = bytes;
    }

    /// <summary>Resets the hit/miss counters (not the stored entries).</summary>
    public void ResetCounters()
    {
        Hits = 0;
        Misses = 0;
    }
}
