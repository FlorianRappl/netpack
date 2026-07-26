namespace NetPack.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using NetPack;
using NetPack.Graph;
using Xunit;

public class SolidTests
{
    private static async Task<bool> DetectSolid(string packageJson)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-solid-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), packageJson);
            // A plain JS entry (no JSX) so detection is exercised without invoking
            // the Node/Babel toolchain.
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export const x = 1;");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            return graph.Context.UseSolid;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Jsx_files_are_treated_as_javascript_modules()
    {
        // `.jsx`/`.tsx` land in a JS bundle; for a Solid project they're compiled by
        // babel-preset-solid over the Node bridge at process time. End-to-end
        // compilation is covered manually since it requires the Babel toolchain.
        Assert.Equal(".js", Helpers.GetType(".jsx"));
        Assert.Equal(".js", Helpers.GetType(".tsx"));
    }

    [Fact]
    public async Task Solid_is_detected_when_solid_js_is_a_dependency()
    {
        Assert.True(await DetectSolid("{\"dependencies\":{\"solid-js\":\"^1.8.0\"}}"));
    }

    [Fact]
    public async Task Solid_detection_defers_to_react_when_both_present()
    {
        // React wins the JSX runtime, so Solid's transform stays off.
        Assert.False(await DetectSolid(
            "{\"dependencies\":{\"solid-js\":\"^1.8.0\",\"react\":\"^18.0.0\"}}"));
    }

    [Fact]
    public async Task Solid_is_off_without_the_dependency()
    {
        Assert.False(await DetectSolid("{}"));
    }
}
