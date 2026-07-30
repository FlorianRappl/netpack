namespace NetPack.Graph;

/// <summary>
/// Named passes in the incremental build pipeline. Each pass represents a
/// discrete stage that can be selectively skipped or recovered from a previous
/// build. Passes run in definition order.
/// </summary>
[Flags]
public enum IncrementalPass
{
    /// <summary>Nothing cached — full cold build.</summary>
    None = 0,

    /// <summary>Build the module graph: read files, parse, discover dependencies, resolve.</summary>
    BuildModuleGraph = 1 << 0,

    /// <summary>Post-processing: CSS modules transform, tree-shaking analysis, React Refresh setup.</summary>
    FinishModules = 1 << 1,

    /// <summary>Chunk graph assembly: Connected.Apply, CSS chunk splitting.</summary>
    BuildChunkGraph = 1 << 2,

    /// <summary>Code generation: JSX lowering, import rewriting, factory assembly (Phase 2).</summary>
    ModulesCodegen = 1 << 3,

    /// <summary>Content hashing: compute output hashes for cache-busting names.</summary>
    ChunksHashes = 1 << 4,

    /// <summary>Render to bytes: printing, mangling, formatting (Phase 3).</summary>
    ChunkAsset = 1 << 5,

    /// <summary>Emit to disk/memory: write final files.</summary>
    EmitAssets = 1 << 6,

    /// <summary>All passes — full cold build.</summary>
    All = BuildModuleGraph | FinishModules | BuildChunkGraph | ModulesCodegen | ChunksHashes | ChunkAsset | EmitAssets,
}

/// <summary>
/// Holds artifacts produced by each incremental pass, so warm rebuilds can
/// recover previous results instead of recomputing. Artifacts are keyed by
/// pass and a stable identifier (e.g. module file name, bundle name).
/// </summary>
public class PassContext
{
    private readonly Dictionary<string, object> _artifacts = [];

    /// <summary>Number of artifact recoveries (hits) since last reset.</summary>
    public int Recoveries { get; private set; }

    /// <summary>Number of artifact stores (misses) since last reset.</summary>
    public int Computes { get; private set; }

    /// <summary>
    /// Attempts to recover a previously-stored artifact for the given pass
    /// and key. Returns the artifact on hit, or null when nothing is cached
    /// for that key.
    /// </summary>
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

    /// <summary>
    /// Stores an artifact produced by a pass for future recovery.
    /// </summary>
    public void Store(IncrementalPass pass, string key, object artifact)
    {
        var fullKey = $"{(int)pass}:{key}";
        _artifacts[fullKey] = artifact;
        Computes++;
    }

    /// <summary>
    /// Tests whether any artifact is stored for the given pass and key.
    /// </summary>
    public bool Has(IncrementalPass pass, string key)
    {
        var fullKey = $"{(int)pass}:{key}";
        return _artifacts.ContainsKey(fullKey);
    }

    /// <summary>Resets the recovery/compute counters.</summary>
    public void ResetCounters()
    {
        Recoveries = 0;
        Computes = 0;
    }

    /// <summary>Removes all stored artifacts.</summary>
    public void Clear()
    {
        _artifacts.Clear();
        ResetCounters();
    }
}
