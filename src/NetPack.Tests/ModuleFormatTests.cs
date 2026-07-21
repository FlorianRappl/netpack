namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

public class ModuleFormatTests
{
    [Fact]
    public void Factory_returns_the_esm_format()
    {
        Assert.IsType<EsmModuleFormat>(JsModuleFormats.For(ModuleFormat.Esm));
    }

    [Theory]
    [InlineData(ModuleFormat.CommonJs)]
    [InlineData(ModuleFormat.Umd)]
    [InlineData(ModuleFormat.SystemJs)]
    public void Factory_rejects_formats_that_are_not_implemented_yet(ModuleFormat format)
    {
        var ex = Assert.Throws<NotSupportedException>(() => JsModuleFormats.For(format));
        Assert.Contains("not supported yet", ex.Message);
    }

    [Fact]
    public async Task Esm_format_bundles_and_exports_through_the_abstraction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-fmt-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "export const value = 1 + 2;\nexport default value;");

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions
            {
                IsOptimizing = false,
                IsReloading = false,
                Format = ModuleFormat.Esm,
            });

            // The ESM envelope still emits the root exports and stays valid JS.
            Assert.Contains("export default", output);
            Assert.Contains("export {", output);
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
