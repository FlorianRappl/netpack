namespace NetPack.Plugins;

/// <summary>
/// Determines how taps are executed within a hook.
/// Inspired by rspack's hook system.
/// </summary>
public enum HookKind
{
    /// <summary>Runs each tap sequentially in stage order. Stops on error.</summary>
    Series,

    /// <summary>Runs each tap sequentially. Each tap returns <c>Option&lt;T&gt;</c>.
    /// Short-circuits on the first non-null result.</summary>
    SeriesBail,

    /// <summary>Like Series, but the first argument is threaded through:
    /// each tap receives the previous tap's output as its input.</summary>
    SeriesWaterfall,

    /// <summary>Runs all taps concurrently via <c>Task.WhenAll</c>.</summary>
    Parallel,

    /// <summary>Synchronous version of Series (no async).</summary>
    Sync,
}
