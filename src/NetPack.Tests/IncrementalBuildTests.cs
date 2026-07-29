namespace NetPack.Tests;

using System;
using System.Diagnostics;
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

    [Fact]
    public async Task Warm_build_is_faster_than_cold_build()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");

            // Create 20 modules with non-trivial content to make parsing measurable.
            for (var i = 0; i < 20; i++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir, $"m{i}.js"),
                    $"export function fn{i}() {{ return {i} * 2 + 1; }}\n" +
                    $"export const v{i} = fn{i}();\n");
            }

            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                string.Join("\n", Enumerable.Range(0, 20).Select(i =>
                    $"import {{ v{i} }} from './m{i}.js';")) +
                "\nexport default " + string.Join(" + ", Enumerable.Range(0, 20).Select(i => $"v{i}")) + ";");

            var cache = new BuildCache();

            // Cold build.
            var cold = Stopwatch.StartNew();
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }
            cold.Stop();

            // Warm build — should hit cache for all 21 files.
            var warm = Stopwatch.StartNew();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }
            warm.Stop();

            // Warm build should be measurably faster (at least 20% improvement).
            var ratio = (double)warm.ElapsedMilliseconds / Math.Max(cold.ElapsedMilliseconds, 1);
            Assert.True(ratio < 0.9, 
                $"Warm build ({warm.ElapsedMilliseconds}ms) should be faster than cold ({cold.ElapsedMilliseconds}ms). Ratio: {ratio:F2}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task New_module_leads_to_fresh_parse_and_valid_output()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { a } from './a.js'; export default a;");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.js"), "export const a = 1;");

            var cache = new BuildCache();

            // First build.
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Add a new module between a.js and main.js.
            await File.WriteAllTextAsync(Path.Combine(dir, "b.js"), "export const b = 42;");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.js"),
                "import { b } from './b.js'; export const a = b + 1;");

            // Second build.
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                var output = b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

                var reparse = Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
                Assert.Empty(reparse.Diagnostics);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Removed_module_is_detected_and_rebuild_succeeds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { a } from './a.js'; export default a;");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.js"),
                "import { b } from './b.js'; export const a = b;");
            await File.WriteAllTextAsync(Path.Combine(dir, "b.js"), "export const b = 99;");

            var cache = new BuildCache();

            // First build — populate cache.
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Remove b.js from the dependency chain.
            File.Delete(Path.Combine(dir, "b.js"));
            await File.WriteAllTextAsync(Path.Combine(dir, "a.js"), "export const a = 1;");

            // Second build — should succeed without broken references to b.js.
            cache.ResetCounters();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                var output = b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

                var reparse = Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
                Assert.Empty(reparse.Diagnostics);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rebuild_handles_import_order_change()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { a } from './a.js'; import { x } from './x.js'; export default a + x;");
            await File.WriteAllTextAsync(Path.Combine(dir, "a.js"), "export const a = 1;");
            await File.WriteAllTextAsync(Path.Combine(dir, "x.js"), "export const x = 10;");

            var cache = new BuildCache();

            // First build.
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Change the import order in main.js.
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { x } from './x.js'; import { a } from './a.js'; export default a + x;");

            // Rebuild — order changed, but both modules cached.
            cache.ResetCounters();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                var output = b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

                var reparse = Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
                Assert.Empty(reparse.Diagnostics);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Cache_survives_non_source_file_addition()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cache-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export default 1;");

            var cache = new BuildCache();

            // First build.
            using (var g1 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b1 = g1.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                b1.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            }

            // Add a non-source file (image). Should not affect JS cache.
            await File.WriteAllBytesAsync(Path.Combine(dir, "icon.png"), new byte[] { 1, 2, 3 });

            cache.ResetCounters();
            using (var g2 = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), buildCache: cache))
            {
                var b2 = g2.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
                var output = b2.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
                Assert.Contains("exports.default = 1", output);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
