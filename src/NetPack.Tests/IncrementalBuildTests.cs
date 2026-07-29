namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// Incremental build cache: parsed module ASTs are cached by file-content
/// hash so unchanged files skip re-parsing during rebuilds.
/// </summary>
public class IncrementalBuildTests
{
    [Fact]
    public async Task Second_build_hits_cache_for_unchanged_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { value } from './mod.js'; export default value;");
            await File.WriteAllTextAsync(Path.Combine(dir, "mod.js"),
                "export const value = 42;");

            // First build — populate cache.
            var cache = new BuildCache();
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Second build — should hit cache for unchanged files.
            cache.ResetCounters();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            Assert.True(cache.Hits > 0, $"Expected cache hits > 0, got {cache.Hits}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Changed_file_misses_cache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "export default 1;");

            // First build — populate cache.
            var cache = new BuildCache();
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Modify the file.
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export default 2;");

            // Second build — should miss cache (content changed).
            cache.ResetCounters();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            Assert.True(cache.Misses > 0, $"Expected cache misses > 0 for changed file");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Cache_hit_produces_valid_output()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { value } from './mod.js'; export default value;");
            await File.WriteAllTextAsync(Path.Combine(dir, "mod.js"),
                "export const value = 'hello';");

            // First build (cold) — populate cache.
            var cache = new BuildCache();
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Second build (warm — cache hit).
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                var output2 = b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

                // Both builds should produce valid JavaScript.
                var reparse = Parser.ParseModule(output2, "out.js", new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
                Assert.Empty(reparse.Diagnostics);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Multiple_modules_benefit_from_cache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

            // Create 10 modules.
            for (var i = 0; i < 10; i++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir, $"m{i}.js"),
                    $"export const v{i} = {i};");
            }

            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                string.Join("\n", Enumerable.Range(0, 10).Select(i =>
                    $"import {{ v{i} }} from './m{i}.js';")) +
                "\nexport default " + string.Join(" + ", Enumerable.Range(0, 10).Select(i => $"v{i}")) + ";");

            var cache = new BuildCache();

            // First build.
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            Assert.True(cache.Count >= 10, $"Expected at least 10 cached entries, got {cache.Count}");

            // Second build — all 10 modules + entry should hit cache.
            cache.ResetCounters();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // All modules + entry = 11 files. Double hits possible for re-processed nodes.
            Assert.True(cache.Hits > 0, $"Expected some cache hits among 11 files, got {cache.Hits}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
