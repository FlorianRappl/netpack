namespace NetPack.Graph;

using System.Text.Json;

/// <summary>
/// Reads and writes cache artifacts under <c>node_modules/.cache/netpack/</c>
/// so warm builds survive process restarts. Content-addressed — every artifact
/// is keyed by a hash of its contents, so stale entries are never requested.
/// </summary>
public class PersistentStorage
{
    private readonly string _rootDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PersistentStorage(string projectRoot)
    {
        _rootDir = Path.Combine(projectRoot, "node_modules", ".cache", "netpack");
    }

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

    public async Task WriteJson<T>(string key, T value) where T : class
    {
        var path = GetPath(key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public async ValueTask<byte[]?> ReadBytes(string key)
    {
        var path = GetPath(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    public async Task WriteBytes(string key, byte[] data)
    {
        var path = GetPath(key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, data);
    }

    public bool Exists(string key) => File.Exists(GetPath(key));

    public void Delete(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IEnumerable<string> ListKeys(string prefix)
    {
        var dir = Path.Combine(_rootDir, prefix);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            yield return Path.GetRelativePath(_rootDir, file).Replace('\\', '/');
        }
    }

    private string GetPath(string key) => Path.Combine(_rootDir, key);
}

/// <summary>
/// Saves and loads a <see cref="BuildSnapshot"/> to/from persistent storage
/// under <c>snapshot.json</c>.
/// </summary>
public static class SnapshotPersistence
{
    private const string Key = "snapshot.json";

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

    public static async Task SaveAsync(PersistentStorage storage, BuildSnapshot snapshot)
    {
        var entries = snapshot.GetAllEntries()
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        await storage.WriteJson(Key, entries);
    }
}
