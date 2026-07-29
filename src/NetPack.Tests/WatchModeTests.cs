namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Server;
using Xunit;

/// <summary>
/// Watch-mode stability: debouncing, error recovery, configurable delay, and
/// rebuild responsiveness under rapid edits.
/// </summary>
public class WatchModeTests : IDisposable
{
    private string? _dir;
    private Traverse? _graph;
    private MemoryResultWriter? _writer;
    private FileWatcher<MemoryResultWriter>? _watcher;

    public void Dispose()
    {
        _watcher?.Dispose();
        _graph?.Dispose();
        if (_dir is not null && Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private async Task Setup(string dirPrefix = "netpack-watch-", int debounceMs = 200)
    {
        _dir = Path.Combine(Path.GetTempPath(), dirPrefix + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);

        await File.WriteAllTextAsync(Path.Combine(_dir, "package.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_dir, "main.js"), "export const x = 1;");

        _graph = await Traverse.From(Path.Combine(_dir, "main.js"));
        _writer = new MemoryResultWriter(_graph.Context);
        await _writer.WriteOut(new OutputOptions { IsOptimizing = false, IsReloading = true });

        _watcher = new FileWatcher<MemoryResultWriter>(_writer, debounceMs, _dir);

        // Give the FileSystemWatcher a moment to start listening.
        await Task.Delay(100);
    }

    private void InstallWatcher(Func<Task<MemoryResultWriter>> trigger)
    {
        _watcher!.Install(trigger);
    }

    private string MainJs => Path.Combine(_dir!, "main.js");

    // ------------------------------------------------------------------- tests

    [Fact]
    public async Task Single_file_change_triggers_rebuild()
    {
        await Setup(debounceMs: 100);
        var signal = new TaskCompletionSource<bool>();

        InstallWatcher(async () =>
        {
            var g = await Traverse.From(Path.Combine(_dir!, "main.js"));
            var w = new MemoryResultWriter(g.Context);
            await w.WriteOut(new OutputOptions { IsOptimizing = false, IsReloading = true });
            signal.TrySetResult(true);
            return w;
        });

        await File.WriteAllTextAsync(MainJs, "export const x = 2;");
        var completed = await Task.WhenAny(signal.Task, Task.Delay(3000));
        Assert.True(completed == signal.Task, "Rebuild was not triggered within 3 seconds.");
    }

    [Fact]
    public async Task Rapid_edits_within_debounce_only_trigger_one_rebuild()
    {
        await Setup(debounceMs: 400);
        var count = 0;

        InstallWatcher(async () =>
        {
            Interlocked.Increment(ref count);
            var g = await Traverse.From(Path.Combine(_dir!, "main.js"));
            var w = new MemoryResultWriter(g.Context);
            await w.WriteOut(new OutputOptions { IsOptimizing = false, IsReloading = true });
            return w;
        });

        // Write 5 times rapidly inside the debounce window.
        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(MainJs, $"export const x = {i};");
            await Task.Delay(20);
        }

        // Wait for debounce to settle + rebuild + any trailing FSW events.
        await Task.Delay(2000);

        // Debouncing ensures significantly fewer rebuilds than individual writes.
        // FileSystemWatcher may fire duplicate Changed events, so we accept ≤ 3
        // rather than exactly 1.
        Assert.True(Volatile.Read(ref count) <= 3,
            $"Expected ≤ 3 rebuilds from debounced batch, got {count}");
    }

    [Fact]
    public async Task Change_to_file_outside_project_is_ignored()
    {
        await Setup(debounceMs: 100);
        var signal = new TaskCompletionSource<bool>();

        InstallWatcher(async () =>
        {
            signal.TrySetResult(true);
            var g = await Traverse.From(Path.Combine(_dir!, "main.js"));
            var w = new MemoryResultWriter(g.Context);
            await w.WriteOut(new OutputOptions { IsOptimizing = false, IsReloading = true });
            return w;
        });

        // Write to a file in the temp directory, outside the project root.
        var outsideFile = Path.Combine(Path.GetTempPath(), "unrelated-" + Path.GetRandomFileName() + ".js");
        await File.WriteAllTextAsync(outsideFile, "hello");
        File.Delete(outsideFile);

        var completed = await Task.WhenAny(signal.Task, Task.Delay(600));
        Assert.False(completed == signal.Task, "File change outside project root should not trigger rebuild.");
    }

    [Fact]
    public async Task Configurable_debounce_delay_respects_setting()
    {
        await Setup(debounceMs: 1500);
        var signal = new TaskCompletionSource<bool>();

        InstallWatcher(async () =>
        {
            signal.TrySetResult(true);
            var g = await Traverse.From(Path.Combine(_dir!, "main.js"));
            var w = new MemoryResultWriter(g.Context);
            await w.WriteOut(new OutputOptions { IsOptimizing = false, IsReloading = true });
            return w;
        });

        await File.WriteAllTextAsync(MainJs, "export const x = 2;");

        // At 600ms, within the 1500ms debounce, rebuild should NOT have fired.
        await Task.Delay(600);
        Assert.False(signal.Task.IsCompleted, "Rebuild fired too early — debounce delay not respected.");

        // By 3000ms, it should have fired.
        var completed = await Task.WhenAny(signal.Task, Task.Delay(3000));
        Assert.True(completed == signal.Task, "Rebuild was not triggered after debounce.");
    }

    [Fact]
    public async Task Error_recovery_broken_file_still_watches()
    {
        await Setup(debounceMs: 200);
        var rebuildCount = 0;

        InstallWatcher(async () =>
        {
            Interlocked.Increment(ref rebuildCount);
            // Intentionally throw on the first rebuild to simulate error.
            var count = Volatile.Read(ref rebuildCount);
            if (count == 1)
            {
                throw new InvalidOperationException("Simulated build failure");
            }

            var g = await Traverse.From(Path.Combine(_dir!, "main.js"));
            var w = new MemoryResultWriter(g.Context);
            await w.WriteOut(new OutputOptions { IsOptimizing = false, IsReloading = true });
            return w;
        });

        // First change — will fail.
        await File.WriteAllTextAsync(MainJs, "export const x = 999;");
        await Task.Delay(500);

        // Second change — should succeed (watcher kept running after error).
        var signal = new TaskCompletionSource<bool>();
        await File.WriteAllTextAsync(MainJs, "export const x = 42;");
        await Task.Delay(800);

        Assert.True(Volatile.Read(ref rebuildCount) >= 2,
            $"Expected ≥2 rebuild attempts (1 fail + 1 success), got {Volatile.Read(ref rebuildCount)}");
    }
}
