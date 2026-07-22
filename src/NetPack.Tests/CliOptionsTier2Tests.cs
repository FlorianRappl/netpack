namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

public class CliOptionsTier2Tests
{
    private static JsBundle Primary(Traverse graph)
        => graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-t2-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Public_path_prefixes_asset_references()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png';\nexport default url;");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[] { 1, 2, 3, 4 });

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var output = Primary(graph).Stringify(new OutputOptions
            {
                IsOptimizing = false,
                IsReloading = false,
                PublicPath = "https://cdn.example.com/app",
            });

            Assert.Contains("https://cdn.example.com/app/logo.", output);
            Assert.DoesNotContain("\"./logo.", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task No_public_path_keeps_references_relative()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png';\nexport default url;");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[] { 1, 2, 3, 4 });

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var output = Primary(graph).Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("./logo.", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Conditions_default_without_the_custom_condition()
    {
        var (without, with) = await ResolveWithConditions();
        Assert.Contains("DEFAULT_ENTRY", without);
        Assert.DoesNotContain("CUSTOM_ENTRY", without);
        Assert.Contains("CUSTOM_ENTRY", with);
        Assert.DoesNotContain("DEFAULT_ENTRY", with);
    }

    private static async Task<(string Without, string With)> ResolveWithConditions()
    {
        var dir = Dir();
        var pkg = Path.Combine(dir, "node_modules", "mypkg");
        Directory.CreateDirectory(pkg);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { v } from 'mypkg';\nexport default v;");
            await File.WriteAllTextAsync(Path.Combine(pkg, "package.json"),
                "{\"name\":\"mypkg\",\"version\":\"1.0.0\",\"exports\":{\".\":{\"custom\":\"./custom.mjs\",\"default\":\"./default.mjs\"}}}");
            await File.WriteAllTextAsync(Path.Combine(pkg, "custom.mjs"), "export const v = 'CUSTOM_ENTRY';");
            await File.WriteAllTextAsync(Path.Combine(pkg, "default.mjs"), "export const v = 'DEFAULT_ENTRY';");

            var opts = new OutputOptions { IsOptimizing = false, IsReloading = false };

            using var plain = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var without = Primary(plain).Stringify(opts);

            using var custom = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(),
                conditions: new[] { "custom" });
            var with = Primary(custom).Stringify(opts);

            return (without, with);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Packages_external_keeps_bare_imports_external()
    {
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            // 'some-pkg' is not installed; --packages=external must not try to resolve it.
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import x from 'some-pkg';\nimport { local } from './local.js';\nexport default [x, local];");
            await File.WriteAllTextAsync(Path.Combine(dir, "local.js"), "export const local = 'LOCAL_BUNDLED';");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(),
                externalPackages: true);
            var output = Primary(graph).Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("some-pkg", output);       // kept as an external import
            Assert.Contains("LOCAL_BUNDLED", output);   // the relative import is still bundled
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(ModuleFormat.Esm)]
    [InlineData(ModuleFormat.CommonJs)]
    [InlineData(ModuleFormat.Umd)]
    [InlineData(ModuleFormat.SystemJs)]
    public async Task Public_path_applies_across_output_formats(ModuleFormat format)
    {
        // A fresh graph per format — the lowering mutates the AST in place, so a
        // bundle is stringified once.
        var dir = Dir();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import url from './logo.png';\nexport default url;");
            await File.WriteAllBytesAsync(Path.Combine(dir, "logo.png"), new byte[] { 9, 8, 7 });

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var output = Primary(graph).Stringify(new OutputOptions
            {
                IsOptimizing = false,
                IsReloading = false,
                Format = format,
                PublicPath = "/static",
            });

            Assert.Contains("/static/logo.", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
