namespace NetPack.Graph;

/// <summary>
/// Named stages in the incremental build pipeline that can be selectively
/// skipped or recovered from a previous build.
/// </summary>
[Flags]
public enum IncrementalPass
{
    None = 0,
    BuildModuleGraph = 1 << 0,
    FinishModules = 1 << 1,
    BuildChunkGraph = 1 << 2,
    ModulesCodegen = 1 << 3,
    ChunksHashes = 1 << 4,
    ChunkAsset = 1 << 5,
    EmitAssets = 1 << 6,
    All = BuildModuleGraph | FinishModules | BuildChunkGraph | ModulesCodegen | ChunksHashes | ChunkAsset | EmitAssets,
}

/// <summary>
/// Stores artifacts produced by each pass so warm rebuilds can recover
/// previous results instead of recomputing.
/// </summary>
public class PassContext
{
    private readonly Dictionary<string, object> _artifacts = [];

    public int Recoveries { get; private set; }
    public int Computes { get; private set; }

    public T? Recover<T>(IncrementalPass pass, string key) where T : class
    {
        var fullKey = $"{(int)pass}:{key}";
        if (_artifacts.TryGetValue(fullKey, out var obj) && obj is T artifact)
        {
            Recoveries++;
            return artifact;
        }

        return null;
    }

    public void Store(IncrementalPass pass, string key, object artifact)
    {
        var fullKey = $"{(int)pass}:{key}";
        _artifacts[fullKey] = artifact;
        Computes++;
    }

    public bool Has(IncrementalPass pass, string key)
    {
        var fullKey = $"{(int)pass}:{key}";
        return _artifacts.ContainsKey(fullKey);
    }

    public void ResetCounters()
    {
        Recoveries = 0;
        Computes = 0;
    }

    public void Clear()
    {
        _artifacts.Clear();
        ResetCounters();
    }
}
