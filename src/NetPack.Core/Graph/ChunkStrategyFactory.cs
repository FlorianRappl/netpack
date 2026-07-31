namespace NetPack.Graph;

/// <summary>
/// Factory that creates the correct <see cref="IChunkGroupingStrategy"/> from
/// a <see cref="Config.SplitChunksConfig"/>. When no config is given or it has
/// no <c>cacheGroups</c>, returns <see cref="DefaultChunkStrategy"/> for
/// backward compatibility.
/// </summary>
public static class ChunkStrategyFactory
{
    public static IChunkGroupingStrategy Create(Config.SplitChunksConfig? config)
    {
        if (config?.CacheGroups is null or { Count: 0 })
        {
            return new DefaultChunkStrategy();
        }

        return new SplitChunksStrategy(config);
    }
}
