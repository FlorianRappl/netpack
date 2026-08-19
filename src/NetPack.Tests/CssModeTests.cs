namespace NetPack.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using NetPack.Graph;
using Xunit;

/// <summary>
/// The <c>--css</c> mode is resolved once per build from the entry kind and
/// stored on the context (never <see cref="CssMode.Auto"/> afterward). These
/// cover that resolution; the output behavior per mode is layered on separately.
/// </summary>
public class CssModeTests
{
    private static async Task<CssMode> Resolve(string entryName, string entryContent, CssMode mode)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-css-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, entryName), entryContent);

            using var graph = await Traverse.From(
                Path.Combine(dir, entryName), Array.Empty<string>(), Array.Empty<string>(), cssMode: mode);

            return graph.Context.CssMode;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Auto_resolves_to_link_for_an_html_entry()
    {
        var resolved = await Resolve("index.html",
            "<!doctype html><html><head></head><body></body></html>", CssMode.Auto);

        Assert.Equal(CssMode.Link, resolved);
    }

    [Fact]
    public async Task Auto_resolves_to_none_for_a_js_entry()
    {
        var resolved = await Resolve("index.js", "export const x = 1;", CssMode.Auto);

        Assert.Equal(CssMode.None, resolved);
    }

    [Theory]
    [InlineData(CssMode.Link)]
    [InlineData(CssMode.Style)]
    [InlineData(CssMode.None)]
    [InlineData(CssMode.Export)]
    public async Task Explicit_mode_is_preserved(CssMode mode)
    {
        Assert.Equal(mode, await Resolve("index.js", "export const x = 1;", mode));
        Assert.Equal(mode, await Resolve("index.html", "<!doctype html><html><body></body></html>", mode));
    }
}
