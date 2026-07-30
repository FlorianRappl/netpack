namespace NetPack.Graph;

using System.Collections.Concurrent;

/// <summary>
/// Records every module processed during a build (file path → content hash),
/// so the next build can detect which files changed without re-hashing.
/// </summary>
public class BuildSnapshot
{
    private readonly ConcurrentDictionary<string, string> _entries = [];

    public int Count => _entries.Count;

    public void Record(string filePath, string contentHash)
    {
        _entries[filePath] = contentHash;
    }

    public bool HasChanged(string filePath, string currentHash)
    {
        return !_entries.TryGetValue(filePath, out var oldHash) || oldHash != currentHash;
    }

    public string? GetHash(string filePath)
    {
        return _entries.TryGetValue(filePath, out var hash) ? hash : null;
    }

    public bool Contains(string filePath) => _entries.ContainsKey(filePath);

    public IReadOnlyDictionary<string, string> GetAllEntries() => _entries;
}

/// <summary>
/// Tracks what changed between builds — added, removed, or modified files.
/// </summary>
public class MutationSet
{
    public List<string> Added { get; } = [];
    public List<string> Removed { get; } = [];
    public List<string> Changed { get; } = [];
    public List<string> DependencyChanged { get; } = [];

    public int TotalCount => Added.Count + Removed.Count + Changed.Count + DependencyChanged.Count;

    public bool IsEmpty => TotalCount == 0;

    public bool IsAffected(string filePath)
    {
        return Added.Contains(filePath)
            || Removed.Contains(filePath)
            || Changed.Contains(filePath)
            || DependencyChanged.Contains(filePath);
    }
}
