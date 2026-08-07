namespace NetPack.Tests;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

/// <summary>
/// Type-only imports carry no runtime module: <c>import type { X } from 'pkg'</c>
/// (and imports whose every specifier is an individual <c>type</c>) must be erased
/// entirely — never resolved into the graph, never lowered into a runtime require /
/// external / SystemJS setter. Regression test for a type-only import leaking a
/// dependency and a `const { X } = …` destructure into the output.
/// </summary>
public class TypeOnlyImportBundlingTests
{
    private static async Task<string> BundleEntry(string source, ModuleFormat format)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-typeonly-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.ts"), source);

            using var graph = await Traverse.From(Path.Combine(dir, "index.ts"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false, Format = format });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(ModuleFormat.SystemJs)]
    [InlineData(ModuleFormat.Esm)]
    [InlineData(ModuleFormat.CommonJs)]
    public async Task Type_only_import_is_fully_erased(ModuleFormat format)
    {
        // 'sample-piral' isn't installed; if the type-only import were treated as
        // a runtime dependency this would also surface as an unresolved external.
        var output = await BundleEntry(
            "import type { PiletApi } from 'sample-piral';\n" +
            "export function setup(app: PiletApi) {}\n",
            format);

        Assert.DoesNotContain("sample-piral", output);
        Assert.DoesNotContain("PiletApi", output);
        Assert.Contains("function setup(app)", output);
    }

    [Fact]
    public async Task Import_with_only_type_specifiers_is_erased()
    {
        var output = await BundleEntry(
            "import { type PiletApi } from 'sample-piral';\n" +
            "export const value = 42;\n",
            ModuleFormat.Esm);

        Assert.DoesNotContain("sample-piral", output);
        Assert.DoesNotContain("PiletApi", output);
        Assert.Contains("value", output);
    }

    [Fact]
    public async Task Mixed_type_and_value_specifiers_keep_only_the_value()
    {
        // './lib' is a real local module; only its value export should be
        // destructured — the `type` member must be stripped.
        var dir = Path.Combine(Path.GetTempPath(), "netpack-typeonly-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "lib.ts"),
                "export const helper = 1;\nexport type Helper = number;\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.ts"),
                "import { type Helper, helper } from './lib';\nexport const x = helper;\n");

            using var graph = await Traverse.From(Path.Combine(dir, "index.ts"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("helper", output);
            Assert.DoesNotContain("Helper", output);

            // The emitted bundle must still be valid JavaScript.
            var reparsed = Parser.ParseModule(output, "out.js", new ParserOptions { TypeScript = false, Jsx = false });
            Assert.Empty(reparsed.Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
