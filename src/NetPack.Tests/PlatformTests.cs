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
