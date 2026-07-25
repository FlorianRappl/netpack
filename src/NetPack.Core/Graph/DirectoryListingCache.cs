namespace NetPack.Graph;

using System.Collections.Concurrent;

/// <summary>
/// Caches directory listings so extensionless resolution does not re-scan the
/// same folder repeatedly during a build. Watch mode can invalidate affected
/// directories before the next rebuild.
/// </summary>
public sealed class DirectoryListingCache
{
    private readonly ConcurrentDictionary<string, string[]> _files = new(System.StringComparer.Ordinal);

    public string[] GetFiles(string directory)
        => _files.GetOrAdd(directory, static dir => Directory.GetFiles(dir));

    public void Invalidate(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var directory = Directory.Exists(path)
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))
            : Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            _files.TryRemove(directory, out _);
        }
    }

    public void Clear() => _files.Clear();
}