namespace NetPack.Graph;

using System.Collections.Concurrent;

/// <summary>
/// Stores rendered bundle bytes keyed by a hash of all module content hashes
/// in the bundle, so unchanged chunks skip the entire render pipeline
/// (printing, mangling, formatting) during warm rebuilds.
/// </summary>
public class RenderCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = [];

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Count => _entries.Count;

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

    public void Put(string key, byte[] bytes)
    {
        _entries[key] = bytes;
    }

    public void ResetCounters()
    {
        Hits = 0;
        Misses = 0;
    }
}
