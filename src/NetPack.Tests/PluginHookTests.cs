namespace NetPack.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using NetPack.Plugins;
using Xunit;

/// <summary>
/// Tests for the plugin hook system.
/// </summary>
public class PluginHookTests
{
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

    // -- Parallel Hook -----------------------------------------------------

    [Fact]
    public async Task ParallelHook_runs_all_taps_concurrently()
    {
        var hook = new ParallelHook<string>();
        var results = new List<string>();
        var allDone = new TaskCompletionSource();

        hook.Tap(new ConcurrentTap(0, "a", results, allDone));
        hook.Tap(new ConcurrentTap(0, "b", results, allDone));
        hook.Tap(new ConcurrentTap(0, "c", results, allDone));

        await hook.CallAsync("ctx");

        Assert.Equal(3, results.Count);
        Assert.Contains("a", results);
        Assert.Contains("b", results);
        Assert.Contains("c", results);
    }

    // -- Plugin Registration -----------------------------------------------

    [Fact]
    public void PluginDriver_registers_plugins_and_calls_apply()
    {
        var driver = new PluginDriver();
        var plugin = new TestPlugin("test-plugin");

        driver.Add(plugin);

        Assert.Single(driver.Plugins);
        Assert.Equal("test-plugin", driver.Plugins[0].Name);
        Assert.True(plugin.Applied);
    }

    [Fact]
    public void PluginDriver_can_register_multiple_plugins()
    {
        var driver = new PluginDriver();

        driver.Add(new TestPlugin("p1"));
        driver.Add(new TestPlugin("p2"));
        driver.Add(new TestPlugin("p3"));

        Assert.Equal(3, driver.Plugins.Count);
    }

    [Fact]
    public async Task Plugin_taps_are_called_through_driver_hooks()
    {
        var driver = new PluginDriver();
        var results = new List<string>();

        driver.Add(new RecordingPlugin(results, 10, "first"));
        driver.Add(new RecordingPlugin(results, 5, "second"));

        await driver.CompilerHooks.Compilation.CallAsync(new CompilationContext
        {
            BundlerContext = null!,
            OutputOptions = null!,
        });

        // Second plugin has lower stage, should run first
        Assert.Equal(["second", "first"], results);
    }

    // -- Test Helpers ------------------------------------------------------

    private class TapImpl(int stage, string name, List<string> results) : IAsyncHookTap<string>
    {
        public int Stage => stage;
        public Task RunAsync(string context) { results.Add(name); return Task.CompletedTask; }
    }

    private class CountTap(int stage, List<string> results) : IAsyncHookTap<string>
    {
        public int Stage => stage;
        public Task RunAsync(string context) { results.Add("count"); return Task.CompletedTask; }
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

    private class ConcurrentTap(int stage, string name, List<string> results, TaskCompletionSource allDone) : IAsyncHookTap<string>
    {
        public int Stage => stage;
        public async Task RunAsync(string context)
        {
            await Task.Delay(1);
            lock (results) { results.Add(name); }
        }
    }

    private class TestPlugin(string name) : IPlugin
    {
        public string Name => name;
        public bool Applied { get; private set; }
        public void Apply(IApplyContext context) { Applied = true; }
    }

    private class RecordingPlugin(List<string> results, int stage, string name) : IPlugin
    {
        public string Name => name;
        public void Apply(IApplyContext context)
        {
            context.CompilerHooks.Compilation.Tap(new PluginTap(results, stage, name));
        }
    }

    private class PluginTap(List<string> results, int stage, string name) : IAsyncHookTap<CompilationContext>
    {
        public int Stage => stage;
        public Task RunAsync(CompilationContext context) { results.Add(name); return Task.CompletedTask; }
    }
}
