namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// Multi-step rebuild test helper. Convenience wrapper for the common test
/// pattern: build → edit → rebuild → assert → edit → rebuild → assert ...
///
/// Usage:
/// <code>
/// using var test = new IncrementalTestHelper();
/// await test.Setup("entry.js", ("a.js", "export const a = 1;"));
///
/// var output0 = await test.Build();
/// test.AssertValidJs();
///
/// await test.Edit("a.js", "export const a = 2;");
/// var output1 = await test.Rebuild();
/// test.AssertValidJs();
/// test.AssertOutputContains(1, "a = 2");
/// </code>
/// </summary>
public class IncrementalTestHelper : IDisposable
{
    private string _dir = null!;
    private string _entry = null!;
    private BuildCache? _cache;
    private CodegenCache? _codegenCache;
    private RenderCache? _renderCache;
    private PassContext? _passContext;
    private BuildSnapshot? _snapshot;
    private PersistentStorage? _persistentStorage;
    private ModuleIdMap? _moduleIds;
    private string _lastOutput = "";
    private int _step;
    private readonly List<string> _outputs = [];
    private string? _snapshotDir;
    private bool _updateSnapshots;

    /// <summary>All outputs from each build step (0 = initial, 1, 2, ...).</summary>
    public IReadOnlyList<string> Outputs => _outputs;

    /// <summary>The current step number (0-indexed).</summary>
    public int Step => _step;

    /// <summary>Cache hits from the last build (Phase 1 parse cache).</summary>
    public int CacheHits => _cache?.Hits ?? 0;

    /// <summary>Cache misses from the last build (Phase 1 parse cache).</summary>
    public int CacheMisses => _cache?.Misses ?? 0;

    /// <summary>Phase 2 codegen cache hits from the last build.</summary>
    public int CodegenHits => _codegenCache?.Hits ?? 0;

    /// <summary>Phase 2 codegen cache misses from the last build.</summary>
    public int CodegenMisses => _codegenCache?.Misses ?? 0;

    /// <summary>Phase 3 render cache hits from the last build.</summary>
    public int RenderHits => _renderCache?.Hits ?? 0;

    /// <summary>Phase 3 render cache misses from the last build.</summary>
    public int RenderMisses => _renderCache?.Misses ?? 0;

    /// <summary>Phase 4 pass context recoveries (artifact hits).</summary>
    public int PassRecoveries => _passContext?.Recoveries ?? 0;

    /// <summary>Phase 4 pass context computes (artifact stores).</summary>
    public int PassComputes => _passContext?.Computes ?? 0;

    /// <summary>Phase 5 snapshot: number of modules recorded in the snapshot.</summary>
    public int SnapshotCount => _snapshot?.Count ?? 0;

    /// <summary>
    /// Phase 6: enables persistent storage under <c>node_modules/.cache/netpack/</c>
    /// in the temp project directory. On warm builds, the snapshot is loaded
    /// from disk; on build completion, it's saved.
    /// </summary>
    public void EnablePersistentStorage()
    {
        _persistentStorage = new PersistentStorage(_dir);
    }

    /// <summary>
    /// Phase 6: saves the current snapshot to persistent storage (called
    /// automatically after each build when persistent storage is enabled).
    /// </summary>
    public async Task SaveSnapshotToDisk()
    {
        if (_persistentStorage is not null && _snapshot is not null)
        {
            await SnapshotPersistence.SaveAsync(_persistentStorage, _snapshot);
        }
    }

    /// <summary>
    /// Phase 6: loads a previously-saved snapshot from persistent storage.
    /// Replaces the current in-memory snapshot.
    /// </summary>
    public async Task LoadSnapshotFromDisk()
    {
        if (_persistentStorage is not null)
        {
            _snapshot = await SnapshotPersistence.LoadAsync(_persistentStorage);
        }
    }

    /// <summary>
    /// Phase 5: produces a mutation set by diffing the previous snapshot
    /// against the current file system. Each file in the snapshot is hashed
    /// and compared.
    /// </summary>
    public MutationSet ComputeMutations()
    {
        var mutations = new MutationSet();

        if (_snapshot is null)
        {
            return mutations;
        }

        // Check recorded files for changes.
        foreach (var (filePath, oldHash) in _snapshot.GetAllEntries())
        {
            if (!File.Exists(filePath))
            {
                mutations.Removed.Add(filePath);
            }
            else
            {
                var currentHash = HashFile(filePath);
                if (currentHash != oldHash)
                {
                    mutations.Changed.Add(filePath);
                }
            }
        }

        // Check for new files not in the snapshot.
        foreach (var file in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
        {
            if (!_snapshot.Contains(file) && IsSourceFile(file))
            {
                mutations.Added.Add(file);
            }
        }

        return mutations;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return NetPack.Hash.ComputeHash(stream).GetAwaiter().GetResult();
    }

    private static bool IsSourceFile(string path)
    {
        var fileName = Path.GetFileName(path);
        // Exclude package.json and other config files.
        if (fileName == "package.json" || fileName.StartsWith('.'))
        {
            return false;
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".js" or ".jsx" or ".ts" or ".tsx" or ".css" or ".html" or ".vue" or ".svelte" or ".astro" or ".json";
    }

    /// <summary>
    /// Creates a temporary project directory and writes the entry file plus
    /// any additional source files. A <c>package.json</c> is written
    /// automatically.
    /// </summary>
    public async Task Setup(string entry, params (string Name, string Content)[] files)
    {
        _dir = Path.Combine(Path.GetTempPath(), "netpack-incr-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _entry = entry;

        await File.WriteAllTextAsync(Path.Combine(_dir, "package.json"), "{}");

        foreach (var (name, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(_dir, name), content);
        }
    }

    /// <summary>
    /// Runs a build with incremental cache support. The first call creates and
    /// populates both the Phase 1 parse cache and Phase 2 codegen cache;
    /// subsequent calls reuse them for faster rebuilds.
    /// When <paramref name="useStableIds"/> is true, a persistent
    /// <see cref="ModuleIdMap"/> is shared across builds so module ids stay
    /// stable — required for Phase 2 codegen cache hits.
    /// </summary>
    public async Task<string> Build(OutputOptions? options = null, bool useStableIds = false, bool enableRenderCache = true)
    {
        options ??= new OutputOptions { IsOptimizing = false, IsReloading = false };
        _cache ??= new BuildCache();
        _codegenCache ??= new CodegenCache();
        _renderCache ??= new RenderCache();
        _passContext ??= new PassContext();
        _snapshot ??= new BuildSnapshot();
        _cache.ResetCounters();
        _codegenCache.ResetCounters();
        _renderCache.ResetCounters();
        _passContext.ResetCounters();

        // Phase 6: auto-load snapshot from persistent storage on first build.
        if (_persistentStorage is not null && _snapshot.Count == 0)
        {
            _snapshot = await SnapshotPersistence.LoadAsync(_persistentStorage);
        }

        if (useStableIds)
        {
            _moduleIds ??= new ModuleIdMap();
        }

        using var graph = await Traverse.From(
            Path.Combine(_dir, _entry), Array.Empty<string>(), Array.Empty<string>(),
            moduleIds: _moduleIds,
            buildCache: _cache,
            codegenCache: _codegenCache,
            renderCache: enableRenderCache ? _renderCache : null,
            passContext: _passContext,
            snapshot: _snapshot);

        var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
        _lastOutput = bundle.Stringify(options);
        _outputs.Add(_lastOutput);
        _step++;

        // Phase 6: auto-save snapshot to persistent storage after each build.
        if (_persistentStorage is not null)
        {
            await SnapshotPersistence.SaveAsync(_persistentStorage, _snapshot);
        }

        return _lastOutput;
    }

    /// <summary>Alias for <see cref="Build"/> — rebuild after edits.</summary>
    public Task<string> Rebuild(OutputOptions? options = null, bool useStableIds = false, bool enableRenderCache = true) => Build(options, useStableIds, enableRenderCache);

    /// <summary>Overwrites a source file.</summary>
    public async Task Edit(string fileName, string newContent)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, fileName), newContent);
    }

    /// <summary>Creates a new source file in the project directory.</summary>
    public async Task AddFile(string fileName, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, fileName), content);
    }

    /// <summary>Deletes a source file from the project directory.</summary>
    public void DeleteFile(string fileName)
    {
        File.Delete(Path.Combine(_dir, fileName));
    }

    /// <summary>
    /// Re-parses the last build output and asserts zero diagnostics.
    /// </summary>
    public void AssertValidJs()
    {
        var reparsed = Parser.ParseModule(_lastOutput, "out.js",
            new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
        Assert.Empty(reparsed.Diagnostics);
    }

    /// <summary>
    /// Asserts that the output from a specific step contains the given
    /// substring. Step 0 = first build.
    /// </summary>
    public void AssertOutputContains(int step, string substring)
    {
        Assert.True(_outputs.Count > step, $"No output recorded for step {step}");
        Assert.Contains(substring, _outputs[step]);
    }

    /// <summary>
    /// Asserts that the output from a specific step does NOT contain the
    /// given substring.
    /// </summary>
    public void AssertOutputDoesNotContain(int step, string substring)
    {
        Assert.True(_outputs.Count > step, $"No output recorded for step {step}");
        Assert.DoesNotContain(substring, _outputs[step]);
    }

    /// <summary>
    /// Asserts that <see cref="CacheHits"/> is within the expected range.
    /// </summary>
    public void AssertCacheHits(int minExpected, int maxExpected = int.MaxValue)
    {
        Assert.True(CacheHits >= minExpected,
            $"Expected at least {minExpected} cache hits, got {CacheHits}");
        Assert.True(CacheHits <= maxExpected,
            $"Expected at most {maxExpected} cache hits, got {CacheHits}");
    }

    // -- snapshot support ----------------------------------------------------

    /// <summary>
    /// Enables rspack-style snapshot verification for this test. Snapshot files
    /// are stored under <c>__snapshots__/&lt;ClassName&gt;.&lt;MethodName&gt;/step_&lt;N&gt;.js</c>.
    /// When <c>NETPACK_UPDATE_SNAPSHOTS=1</c>, existing snapshots are overwritten.
    /// </summary>
    public void EnableSnapshots(string className, string methodName)
    {
        _updateSnapshots = Environment.GetEnvironmentVariable("NETPACK_UPDATE_SNAPSHOTS") == "1";

        var baseDir = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "__snapshots__",
            className,
            methodName);

        _snapshotDir = Path.GetFullPath(baseDir);

        if (_updateSnapshots && Directory.Exists(_snapshotDir))
        {
            Directory.Delete(_snapshotDir, recursive: true);
        }

        if (!Directory.Exists(_snapshotDir))
        {
            Directory.CreateDirectory(_snapshotDir);
        }
    }

    /// <summary>
    /// Saves the current step's output as a snapshot, or compares it against
    /// the stored snapshot. Fails on mismatch (like rspack's
    /// <c>toMatchFileSnapshotSync</c>). Step is the same as the build step
    /// (0 = initial build, 1 = first rebuild, ...).
    /// </summary>
    public void AssertMatchesSnapshot(int step)
    {
        Assert.True(_snapshotDir is not null, "EnableSnapshots() must be called first");
        Assert.True(_outputs.Count > step, $"No output recorded for step {step}");

        var snapshotPath = Path.Combine(_snapshotDir, $"step_{step}.js");
        var current = _outputs[step];

        if (_updateSnapshots || !File.Exists(snapshotPath))
        {
            File.WriteAllText(snapshotPath, current);
            return;
        }

        var expected = File.ReadAllText(snapshotPath);
        Assert.Equal(expected, current);
    }

    /// <summary>
    /// Saves metadata about the last build's cache activity as a structured
    /// snapshot (mirrors rspack's per-step stats). Format: one line per metric.
    /// </summary>
    public void AssertCacheStatsSnapshot(int step, int? expectedCacheHits = null, int? expectedCacheMisses = null, int? expectedCodegenHits = null, int? expectedCodegenMisses = null)
    {
        if (expectedCacheHits.HasValue)
        {
            Assert.True(CacheHits >= expectedCacheHits.Value,
                $"Step {step}: Expected >= {expectedCacheHits} cache hits, got {CacheHits}");
        }

        if (expectedCacheMisses.HasValue)
        {
            Assert.True(CacheMisses >= expectedCacheMisses.Value,
                $"Step {step}: Expected >= {expectedCacheMisses} cache misses, got {CacheMisses}");
        }

        if (expectedCodegenHits.HasValue)
        {
            Assert.True(CodegenHits >= expectedCodegenHits.Value,
                $"Step {step}: Expected >= {expectedCodegenHits} codegen hits, got {CodegenHits}");
        }

        if (expectedCodegenMisses.HasValue)
        {
            Assert.True(CodegenMisses >= expectedCodegenMisses.Value,
                $"Step {step}: Expected >= {expectedCodegenMisses} codegen misses, got {CodegenMisses}");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
