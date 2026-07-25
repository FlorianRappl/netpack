namespace NetPack.Plugins;

/// <summary>
/// A hook that runs taps sequentially in stage order.
/// Inspired by rspack's Series hook.
/// </summary>
public class SeriesHook<TContext>
{
    private readonly List<IAsyncHookTap<TContext>> _taps = [];
    private readonly object _lock = new();

    /// <summary>Number of registered taps.</summary>
    public int Count
    {
        get { lock (_lock) { return _taps.Count; } }
    }

    /// <summary>
    /// Registers a tap. Taps are inserted in sorted order by stage.
    /// </summary>
    public void Tap(IAsyncHookTap<TContext> tap)
    {
        lock (_lock)
        {
            var index = _taps.BinarySearch(tap, Comparer<IAsyncHookTap<TContext>>.Create((a, b) => a.Stage.CompareTo(b.Stage)));
            if (index < 0) index = ~index;
            _taps.Insert(index, tap);
        }
    }

    /// <summary>
    /// Runs all taps sequentially in stage order.
    /// </summary>
    public async Task CallAsync(TContext context)
    {
        IAsyncHookTap<TContext>[] taps;
        lock (_lock)
        {
            taps = [.. _taps];
        }

        foreach (var tap in taps)
        {
            await tap.RunAsync(context);
        }
    }
}

/// <summary>
/// A hook that runs taps sequentially and short-circuits on the first non-null result.
/// Inspired by rspack's SeriesBail hook.
/// </summary>
public class SeriesBailHook<TContext, TResult>
{
    private readonly List<IAsyncBailHookTap<TContext, TResult>> _taps = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) { return _taps.Count; } }
    }

    public void Tap(IAsyncBailHookTap<TContext, TResult> tap)
    {
        lock (_lock)
        {
            var index = _taps.BinarySearch(tap, Comparer<IAsyncBailHookTap<TContext, TResult>>.Create((a, b) => a.Stage.CompareTo(b.Stage)));
            if (index < 0) index = ~index;
            _taps.Insert(index, tap);
        }
    }

    /// <summary>
    /// Runs taps sequentially. Returns the first non-default result, or default if all return default.
    /// </summary>
    public async Task<TResult> CallAsync(TContext context)
    {
        IAsyncBailHookTap<TContext, TResult>[] taps;
        lock (_lock)
        {
            taps = [.. _taps];
        }

        var comparer = EqualityComparer<TResult>.Default;
        foreach (var tap in taps)
        {
            var result = await tap.RunAsync(context);
            if (!comparer.Equals(result, default))
            {
                return result;
            }
        }

        return default!;
    }
}

/// <summary>
/// A hook that threads data through taps: each tap receives the previous output.
/// Inspired by rspack's SeriesWaterfall hook.
/// </summary>
public class SeriesWaterfallHook<TData>
{
    private readonly List<IAsyncWaterfallHookTap<TData>> _taps = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) { return _taps.Count; } }
    }

    public void Tap(IAsyncWaterfallHookTap<TData> tap)
    {
        lock (_lock)
        {
            var index = _taps.BinarySearch(tap, Comparer<IAsyncWaterfallHookTap<TData>>.Create((a, b) => a.Stage.CompareTo(b.Stage)));
            if (index < 0) index = ~index;
            _taps.Insert(index, tap);
        }
    }

    /// <summary>
    /// Runs taps sequentially, threading the data through each tap.
    /// </summary>
    public async Task<TData> CallAsync(TData data)
    {
        IAsyncWaterfallHookTap<TData>[] taps;
        lock (_lock)
        {
            taps = [.. _taps];
        }

        foreach (var tap in taps)
        {
            data = await tap.RunAsync(data);
        }

        return data;
    }
}

/// <summary>
/// A hook that runs all taps concurrently.
/// Inspired by rspack's Parallel hook.
/// </summary>
public class ParallelHook<TContext>
{
    private readonly List<IAsyncHookTap<TContext>> _taps = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) { return _taps.Count; } }
    }

    public void Tap(IAsyncHookTap<TContext> tap)
    {
        lock (_lock)
        {
            var index = _taps.BinarySearch(tap, Comparer<IAsyncHookTap<TContext>>.Create((a, b) => a.Stage.CompareTo(b.Stage)));
            if (index < 0) index = ~index;
            _taps.Insert(index, tap);
        }
    }

    /// <summary>
    /// Runs all taps concurrently.
    /// </summary>
    public async Task CallAsync(TContext context)
    {
        IAsyncHookTap<TContext>[] taps;
        lock (_lock)
        {
            taps = [.. _taps];
        }

        await Task.WhenAll(taps.Select(tap => tap.RunAsync(context)));
    }
}

/// <summary>
/// A synchronous hook that runs taps sequentially.
/// </summary>
public class SyncHook<TContext>
{
    private readonly List<ISyncHookTap<TContext>> _taps = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) { return _taps.Count; } }
    }

    public void Tap(ISyncHookTap<TContext> tap)
    {
        lock (_lock)
        {
            var index = _taps.BinarySearch(tap, Comparer<ISyncHookTap<TContext>>.Create((a, b) => a.Stage.CompareTo(b.Stage)));
            if (index < 0) index = ~index;
            _taps.Insert(index, tap);
        }
    }

    /// <summary>
    /// Runs all taps synchronously in stage order.
    /// </summary>
    public void Call(TContext context)
    {
        ISyncHookTap<TContext>[] taps;
        lock (_lock)
        {
            taps = [.. _taps];
        }

        foreach (var tap in taps)
        {
            tap.Run(context);
        }
    }
}
