namespace NetPack.Graph;

using System.Collections.Concurrent;

/// <summary>
/// Captures the state of all modules processed during a build: each module's
/// file path mapped to its content hash. Used to detect file changes between
/// builds by comparing against the current file system or a subsequent snapshot.
/// </summary>
public class BuildSnapshot
{
    private readonly ConcurrentDictionary<string, string> _entries = [];

    /// <summary>Number of modules recorded in the snapshot.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Records a module's content hash in the snapshot.
    /// </summary>
    public void Record(string filePath, string contentHash)
    {
        _entries[filePath] = contentHash;
    }

    /// <summary>
    /// Returns true when the recorded content hash differs from the given
    /// hash, indicating the file changed since the snapshot was taken.
    /// Returns true also when the file was not in the snapshot (new file).
    /// Returns false when the hash matches (file unchanged).
    /// </summary>
    public bool HasChanged(string filePath, string currentHash)
    {
        return !_entries.TryGetValue(filePath, out var oldHash) || oldHash != currentHash;
    }

    /// <summary>
    /// Returns the recorded content hash for a file, or null if the file
    /// was not in the snapshot.
    /// </summary>
    public string? GetHash(string filePath)
    {
        return _entries.TryGetValue(filePath, out var hash) ? hash : null;
    }

    /// <summary>
    /// Checks whether a file was recorded in the snapshot.
    /// </summary>
    public bool Contains(string filePath) => _entries.ContainsKey(filePath);

    /// <summary>
    /// Returns all recorded entries for diffing/comparison.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllEntries() => _entries;
}

/// <summary>
/// Records what changed between two builds. Produced by diffing a previous
/// <see cref="BuildSnapshot"/> against the current file system or comparing
/// two snapshots. Used by incremental passes to decide what to invalidate.
/// </summary>
public class MutationSet
{
    /// <summary>Files that exist now but were not in the previous snapshot.</summary>
    public List<string> Added { get; } = [];

    /// <summary>Files in the previous snapshot that no longer exist.</summary>
    public List<string> Removed { get; } = [];

    /// <summary>Files whose content hash changed since the previous snapshot.</summary>
    public List<string> Changed { get; } = [];

    /// <summary>Files whose imports (dependency specifiers) changed, even if content didn't.</summary>
    public List<string> DependencyChanged { get; } = [];

    /// <summary>Total number of mutations (added + removed + changed + deps changed).</summary>
    public int TotalCount => Added.Count + Removed.Count + Changed.Count + DependencyChanged.Count;

    /// <summary>True when nothing changed between builds.</summary>
    public bool IsEmpty => TotalCount == 0;

    /// <summary>
    /// Returns true when the given file was affected by any mutation
    /// (its own content, its dependencies, or it was added/removed).
    /// </summary>
    public bool IsAffected(string filePath)
    {
        return Added.Contains(filePath)
            || Removed.Contains(filePath)
            || Changed.Contains(filePath)
            || DependencyChanged.Contains(filePath);
    }
}
