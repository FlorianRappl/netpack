namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

public class PlatformTests
{
    [Theory]
    [InlineData("fs", true)]
    [InlineData("node:fs", true)]
    [InlineData("fs/promises", true)]
    [InlineData("path", true)]
    [InlineData("worker_threads", true)]
    [InlineData("react", false)]
    [InlineData("npm:react", false)]
    public void Node_builtins_are_detected(string specifier, bool expected)
        => Assert.Equal(expected, PlatformTargets.For(Platform.Node).IsBuiltin(specifier));

    [Theory]
    [InlineData("node:fs", true)]
    [InlineData("npm:react", true)]
    [InlineData("jsr:@std/assert", true)]
    [InlineData("fs", false)]
    [InlineData("react", false)]
    public void Deno_schemes_are_detected(string specifier, bool expected)
        => Assert.Equal(expected, PlatformTargets.For(Platform.Deno).IsBuiltin(specifier));

    [Theory]
    [InlineData("node:fs", true)]
    [InlineData("node:fs/promises", true)]
    [InlineData("fs", false)]
    [InlineData("path", false)]
    [InlineData("react", false)]
    public void Node_only_scheme_prefixed_builtins_are_explicit(string specifier, bool expected)
        => Assert.Equal(expected, PlatformTargets.For(Platform.Node).IsExplicitBuiltin(specifier));

    [Theory]
    [InlineData("fs", "node:fs")]
    [InlineData("fs/promises", "node:fs/promises")]
    [InlineData("path", "node:path")]
    [InlineData("test", "node:test")]
    [InlineData("node:fs", null)]      // already prefixed — no fallback
    [InlineData("react", null)]        // not a core module
    public void Node_builtin_fallback_canonicalizes_bare_core_modules(string specifier, string? expected)
        => Assert.Equal(expected, PlatformTargets.For(Platform.Node).BuiltinFallback(specifier));

    [Fact]
    public void Web_and_deno_builtin_fallbacks()
    {
        // The web has no runtime built-ins to canonicalize.
        Assert.Null(PlatformTargets.For(Platform.Web).BuiltinFallback("fs"));

        // Deno's runtime specifiers are always explicitly scheme-prefixed.
        var deno = PlatformTargets.For(Platform.Deno);
        Assert.True(deno.IsExplicitBuiltin("npm:react"));
        Assert.False(deno.IsExplicitBuiltin("react"));
        Assert.Null(deno.BuiltinFallback("fs"));
    }

    [Fact]
    public async Task Node_bare_builtin_is_kept_external_with_node_prefix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-plat-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { readFile } from 'fs';\nexport default readFile;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), platform: Platform.Node);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // The bare `fs` becomes the canonical `node:fs`, kept as a real import.
            Assert.Contains("node:fs", output);
            Assert.DoesNotContain("\"fs\"", output);
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Node_already_prefixed_builtin_is_not_double_prefixed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-plat-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { join } from 'node:path';\nexport default join;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), platform: Platform.Node);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("node:path", output);
            Assert.DoesNotContain("node:node:", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_local_module_shadowing_a_builtin_name_is_bundled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-plat-" + Path.GetRandomFileName());
        var pkg = Path.Combine(dir, "node_modules", "test");
        Directory.CreateDirectory(pkg);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { value } from 'test';\nexport default value;");
            // A local package literally named `test` (which is also `node:test`).
            await File.WriteAllTextAsync(Path.Combine(pkg, "package.json"),
                "{\"name\":\"test\",\"version\":\"1.0.0\",\"main\":\"index.js\"}");
            await File.WriteAllTextAsync(Path.Combine(pkg, "index.js"),
                "export const value = 'LOCAL_TEST_PACKAGE';");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), platform: Platform.Node);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // The local package wins — it is bundled, not treated as node:test.
            Assert.Contains("LOCAL_TEST_PACKAGE", output);
            Assert.DoesNotContain("node:test", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Web_has_no_builtins_and_prefers_the_browser_field()
    {
        var web = PlatformTargets.For(Platform.Web);

        Assert.False(web.IsBuiltin("fs"));
        Assert.False(web.IsBuiltin("node:fs"));
        Assert.True(web.UseBrowserField);
        Assert.False(PlatformTargets.For(Platform.Node).UseBrowserField);
        Assert.False(PlatformTargets.For(Platform.Deno).UseBrowserField);
    }

    [Fact]
    public async Task Node_platform_keeps_builtins_external()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-plat-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { readFile } from 'node:fs/promises';\nexport default readFile;");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(), platform: Platform.Node);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // The built-in stays a bare import instead of being bundled.
            Assert.Contains("node:fs/promises", output);
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
