namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetPack.Plugins;
using Xunit;

/// <summary>
/// Tests for the hook system (the primitives and the compiler/compilation hook
/// containers). There is no plugin driver — hooks are tapped directly.
/// </summary>
public class PluginHookTests
{
    // A compilation context whose payload the taps below never dereference.
    private static CompilationContext Ctx() => new() { BundlerContext = null!, OutputOptions = null! };

    // -- Series Hook -------------------------------------------------------

    [Fact]
    public async Task SeriesHook_runs_taps_in_stage_order()
    {
        var hook = new SeriesHook<string>();
        var results = new List<string>();

        hook.Tap(new TapImpl(20, "c", results));
        hook.Tap(new TapImpl(10, "a", results));
        hook.Tap(new TapImpl(15, "b", results));

        await hook.CallAsync("context");

        Assert.Equal(["a", "b", "c"], results);
    }

    [Fact]
    public async Task SeriesHook_executes_all_taps()
    {
        var hook = new SeriesHook<string>();
        var results = new List<string>();

        hook.Tap(new TapImpl(0, "a", results));
        hook.Tap(new TapImpl(0, "b", results));
        hook.Tap(new TapImpl(0, "c", results));

        await hook.CallAsync("ctx");

        Assert.Equal(3, results.Count);
    }

    // -- SeriesBail Hook ---------------------------------------------------

    [Fact]
    public async Task SeriesBailHook_short_circuits_on_first_result()
    {
        var hook = new SeriesBailHook<string, string>();
        var callOrder = new List<string>();

        hook.Tap(new BailTap(0, "first", callOrder, returnsResult: false));
        hook.Tap(new BailTap(10, "second", callOrder, returnsResult: true));
        hook.Tap(new BailTap(20, "third", callOrder, returnsResult: false));

        var result = await hook.CallAsync("ctx");

        Assert.Equal("second-result", result);
        Assert.Equal(["first", "second"], callOrder); // third was never called
    }

    [Fact]
    public async Task SeriesBailHook_returns_default_when_all_null()
    {
        var hook = new SeriesBailHook<string, int>();

        hook.Tap(new IntBailTap(0, null));
        hook.Tap(new IntBailTap(10, null));

        var result = await hook.CallAsync("ctx");

        Assert.Equal(0, result); // default(int)
    }

    // -- Waterfall Hook ----------------------------------------------------

    [Fact]
    public async Task WaterfallHook_threads_data_through_taps()
    {
        var hook = new SeriesWaterfallHook<string>();

        hook.Tap(new WaterfallTap(0, s => s + "-a"));
        hook.Tap(new WaterfallTap(10, s => s + "-b"));

        var result = await hook.CallAsync("x");

        Assert.Equal("x-a-b", result);
    }

    // -- Parallel Hook -----------------------------------------------------

    [Fact]
    public async Task ParallelHook_runs_all_taps_concurrently()
    {
        var hook = new ParallelHook<string>();
        var results = new List<string>();

        hook.Tap(new ConcurrentTap(0, "a", results));
        hook.Tap(new ConcurrentTap(0, "b", results));
        hook.Tap(new ConcurrentTap(0, "c", results));

        await hook.CallAsync("ctx");

        Assert.Equal(3, results.Count);
        Assert.Contains("a", results);
        Assert.Contains("b", results);
        Assert.Contains("c", results);
    }

    // -- Sync Hook ---------------------------------------------------------

    [Fact]
    public void SyncHook_runs_taps_in_stage_order()
    {
        var hook = new SyncHook<string>();
        var results = new List<string>();

        hook.Tap(new SyncTapImpl(10, "b", results));
        hook.Tap(new SyncTapImpl(5, "a", results));

        hook.Call("ctx");

        Assert.Equal(["a", "b"], results);
    }

    // -- Hook containers ---------------------------------------------------

    [Fact]
    public async Task Compiler_hook_taps_run_in_stage_order()
    {
        var hooks = new CompilerHooks();
        var results = new List<string>();

        hooks.Compilation.Tap(new CtxTap(10, "first", results));
        hooks.Compilation.Tap(new CtxTap(5, "second", results));

        await hooks.Compilation.CallAsync(Ctx());

        Assert.Equal(["second", "first"], results); // lower stage first
    }

    [Fact]
    public async Task Process_assets_orders_by_stage_constants()
    {
        var hooks = new CompilationHooks();
        var order = new List<string>();

        hooks.ProcessAssets.Tap(new CtxTap(ProcessAssetsStage.Report, "report", order));
        hooks.ProcessAssets.Tap(new CtxTap(ProcessAssetsStage.Additional, "additional", order));

        await hooks.ProcessAssets.CallAsync(Ctx());

        Assert.Equal(["additional", "report"], order);
    }

    // -- Test Helpers ------------------------------------------------------

    private class TapImpl(int stage, string name, List<string> results) : IAsyncHookTap<string>
    {
        public int Stage => stage;
        public Task RunAsync(string context) { results.Add(name); return Task.CompletedTask; }
    }

    private class BailTap(int stage, string name, List<string> callOrder, bool returnsResult) : IAsyncBailHookTap<string, string>
    {
        public int Stage => stage;
        public Task<string> RunAsync(string context)
        {
            callOrder.Add(name);
            return Task.FromResult(returnsResult ? $"{name}-result" : null!);
        }
    }

    private class IntBailTap(int stage, int? value) : IAsyncBailHookTap<string, int>
    {
        public int Stage => stage;
        public Task<int> RunAsync(string context) => Task.FromResult(value ?? 0);
    }

    private class WaterfallTap(int stage, Func<string, string> transform) : IAsyncWaterfallHookTap<string>
    {
        public int Stage => stage;
        public Task<string> RunAsync(string data) => Task.FromResult(transform(data));
    }

    private class ConcurrentTap(int stage, string name, List<string> results) : IAsyncHookTap<string>
    {
        public int Stage => stage;
        public async Task RunAsync(string context)
        {
            await Task.Delay(1);
            lock (results) { results.Add(name); }
        }
    }

    private class SyncTapImpl(int stage, string name, List<string> results) : ISyncHookTap<string>
    {
        public int Stage => stage;
        public void Run(string context) => results.Add(name);
    }

    private class CtxTap(int stage, string name, List<string> results) : IAsyncHookTap<CompilationContext>
    {
        public int Stage => stage;
        public Task RunAsync(CompilationContext context) { results.Add(name); return Task.CompletedTask; }
    }
}
