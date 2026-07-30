namespace NetPack.Graph;

using System.Text.Json;

/// <summary>
/// Phase 6 persistent storage: reads and writes cache artifacts to disk under
/// <c>node_modules/.cache/netpack/</c> so warm builds benefit from the previous
/// session's cache. Content-addressed — every artifact is keyed by a hash of
/// its contents, so invalidation is automatic (stale entries are simply never
/// requested again).
/// </summary>
public class PersistentStorage
{
    private readonly string _rootDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Creates storage under the given project root. The cache directory is
    /// <c>{root}/node_modules/.cache/netpack/</c>.
    /// </summary>
    public PersistentStorage(string projectRoot)
    {
        _rootDir = Path.Combine(projectRoot, "node_modules", ".cache", "netpack");
    }

    /// <summary>
    /// Reads a JSON value from disk. Returns null when the key doesn't exist
    /// or the file is corrupted.
    /// </summary>
    public async ValueTask<T?> ReadJson<T>(string key) where T : class
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a JSON value to disk. Creates the directory tree if needed.
    /// </summary>
    public async Task WriteJson<T>(string key, T value) where T : class
    {
        var path = GetPath(key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>
    /// Reads raw bytes from a binary cache file. Returns null when the key
    /// doesn't exist.
    /// </summary>
    public async ValueTask<byte[]?> ReadBytes(string key)
    {
        var path = GetPath(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    /// <summary>
    /// Writes raw bytes to a binary cache file.
    /// </summary>
    public async Task WriteBytes(string key, byte[] data)
    {
        var path = GetPath(key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, data);
    }

    /// <summary>
    /// Checks whether a cache entry exists on disk.
    /// </summary>
    public bool Exists(string key) => File.Exists(GetPath(key));

    /// <summary>
    /// Deletes a cache entry from disk.
    /// </summary>
    public void Delete(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Lists all keys in a sub-path (e.g. "render/").
    /// </summary>
    public IEnumerable<string> ListKeys(string prefix)
    {
        var dir = Path.Combine(_rootDir, prefix);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_rootDir, file).Replace('\\', '/');
            yield return relative;
        }
    }

    private string GetPath(string key) => Path.Combine(_rootDir, key);
}

/// <summary>
/// Saves and loads a <see cref="BuildSnapshot"/> to/from persistent storage.
/// The snapshot is stored as JSON under <c>snapshot.json</c> in the cache
/// directory, mapping absolute file paths to their content hashes.
/// </summary>
public static class SnapshotPersistence
{
    private const string Key = "snapshot.json";

    /// <summary>
    /// Loads a previously-saved snapshot from disk. Returns a new empty
    /// snapshot when nothing is saved yet (first build of a session).
    /// </summary>
    public static async Task<BuildSnapshot> LoadAsync(PersistentStorage storage)
    {
        var entries = await storage.ReadJson<Dictionary<string, string>>(Key);
        var snapshot = new BuildSnapshot();

        if (entries is not null)
        {
            foreach (var (path, hash) in entries)
            {
                snapshot.Record(path, hash);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Saves a snapshot to disk for the next session.
    /// </summary>
    public static async Task SaveAsync(PersistentStorage storage, BuildSnapshot snapshot)
    {
        var entries = snapshot.GetAllEntries()
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        await storage.WriteJson(Key, entries);
    }
}
