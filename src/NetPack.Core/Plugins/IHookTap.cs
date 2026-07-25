namespace NetPack.Plugins;

/// <summary>
/// A tap (handler) registered on a hook. Each tap has a stage number that
/// determines execution order (lower stages run first).
/// </summary>
public interface IHookTap
{
    /// <summary>Execution order — lower values run first.</summary>
    int Stage { get; }
}

/// <summary>
/// An async tap that receives a context parameter.
/// </summary>
public interface IAsyncHookTap<TContext> : IHookTap
{
    Task RunAsync(TContext context);
}

/// <summary>
/// An async tap that returns a result (for SeriesBail hooks).
/// Return default(TResult) to pass; any other value short-circuits.
/// </summary>
public interface IAsyncBailHookTap<TContext, TResult> : IHookTap
{
    Task<TResult> RunAsync(TContext context);
}

/// <summary>
/// An async waterfall tap that transforms the data and returns the transformed value.
/// </summary>
public interface IAsyncWaterfallHookTap<TData> : IHookTap
{
    Task<TData> RunAsync(TData data);
}

/// <summary>
/// A synchronous tap (no async).
/// </summary>
public interface ISyncHookTap<TContext> : IHookTap
{
    void Run(TContext context);
}
