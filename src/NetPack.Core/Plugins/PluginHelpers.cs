namespace NetPack.Plugins;

/// <summary>
/// Convenience base class for async hook taps. Override <see cref="RunAsync"/> to
/// implement your tap logic. Stage defaults to 0.
/// </summary>
public abstract class AsyncHookTap<TContext>(int stage = 0) : IAsyncHookTap<TContext>
{
    public int Stage => stage;

    public abstract Task RunAsync(TContext context);
}

/// <summary>
/// Convenience base class for async bail hook taps.
/// </summary>
public abstract class AsyncBailHookTap<TContext, TResult>(int stage = 0) : IAsyncBailHookTap<TContext, TResult>
{
    public int Stage => stage;

    public abstract Task<TResult> RunAsync(TContext context);
}

/// <summary>
/// Convenience base class for async waterfall hook taps.
/// </summary>
public abstract class AsyncWaterfallHookTap<TData>(int stage = 0) : IAsyncWaterfallHookTap<TData>
{
    public int Stage => stage;

    public abstract Task<TData> RunAsync(TData data);
}

/// <summary>
/// Convenience base class for sync hook taps.
/// </summary>
public abstract class SyncHookTap<TContext>(int stage = 0) : ISyncHookTap<TContext>
{
    public int Stage => stage;

    public abstract void Run(TContext context);
}

/// <summary>
/// Well-known stage constants for process assets, similar to rspack's
/// PROCESS_ASSETS_STAGE_* constants. Lower values run first.
/// </summary>
public static class ProcessAssetsStage
{
    public const int Additional = -2000;
    public const int PreProcess = -1000;
    public const int Derived = -200;
    public const int Additions = -100;
    public const int Optimize = 100;
    public const int OptimizeCount = 200;
    public const int OptimizeCompatibility = 300;
    public const int OptimizeSize = 400;
    public const int DevTooling = 500;
    public const int OptimizeInline = 700;
    public const int Summarize = 1000;
    public const int OptimizeHash = 2500;
    public const int AfterOptimizeHash = 2600;
    public const int OptimizeTransfer = 3000;
    public const int Analyze = 4000;
    public const int Report = 5000;
}

/// <summary>
/// Well-known stage constants for optimize chunks.
/// </summary>
public static class OptimizeChunksStage
{
    public const int Basic = -10;
    public const int Default = 0;
    public const int Advanced = 10;
}
