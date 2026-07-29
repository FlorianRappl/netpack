namespace NetPack;

using System.Collections.Concurrent;
using System.Security.Cryptography;

/// <summary>
/// Incremental build cache. Stores parsed module fragments keyed by file path
/// and content hash, so unchanged files skip re-parsing during rebuilds.
/// </summary>
public class BuildCache
{
    private readonly ConcurrentDictionary<string, CachedEntry> _entries = [];

    /// <summary>
    /// Number of cache hits since the cache was created or reset.
    /// </summary>
    public int Hits { get; private set; }

    /// <summary>
    /// Number of cache misses (files that were not found or had changed content).
    /// </summary>
    public int Misses { get; private set; }

    /// <summary>
    /// Number of stored entries.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Computes a fast hash for file content invalidation. Uses the first 24
    /// bits of SHA256 (3 hex bytes) — same algorithm as <see cref="Hash.ComputeHash"/>.
    /// </summary>
    public static async Task<string> ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return await Hash.ComputeHash(stream);
    }

    /// <summary>
    /// Attempts to retrieve a cached entry for the given file. Returns null
    /// when the file is not cached or its content hash has changed (stale).
    /// </summary>
    public CachedEntry? Get(string filePath, string currentHash)
    {
        if (_entries.TryGetValue(filePath, out var entry) && entry.Hash == currentHash)
        {
            Hits++;
            return entry;
        }

        Misses++;
        return null;
    }

    /// <summary>
    /// Stores or updates a cache entry for a file.
    /// </summary>
    public void Put(string filePath, string hash, object fragment)
    {
        _entries[filePath] = new CachedEntry { Hash = hash, Fragment = fragment };
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
/// A single cached entry: the content hash used as the invalidation key
/// and the parsed fragment.
/// </summary>
public class CachedEntry
{
    public string Hash { get; init; } = "";
    public object Fragment { get; init; } = default!;
}
