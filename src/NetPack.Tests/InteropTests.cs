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
/// Module-interop behavior across the ESM/CommonJS boundary: named and default
/// imports from CommonJS, <c>exports.x</c> assignment, and namespace imports all
/// bundle to valid JavaScript with the referenced values present.
/// </summary>
public class InteropTests
{
    private static async Task<string> Bundle(Action<string> setup)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-interop-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            setup(dir);

            using var graph = await Traverse.From(Path.Combine(dir, "main.js"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
            return output;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Named_imports_from_commonjs_module_exports_object()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "import { foo, bar } from './cjs.js';\nexport const r = foo + bar;");
            File.WriteAllText(Path.Combine(dir, "cjs.js"),
                "module.exports = { foo: 'FOO_VAL', bar: 'BAR_VAL' };");
        });

        Assert.Contains("FOO_VAL", output);
        Assert.Contains("BAR_VAL", output);
    }

    [Fact]
    public async Task Named_import_from_exports_dot_assignment()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { thing } from './cjs.js';\nexport const r = thing;");
            File.WriteAllText(Path.Combine(dir, "cjs.js"), "exports.thing = 'THING_VAL';");
        });

        Assert.Contains("THING_VAL", output);
    }

    [Fact]
    public async Task Default_import_of_esm_default_export()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import d from './esm.js';\nexport const r = d;");
            File.WriteAllText(Path.Combine(dir, "esm.js"), "export default 'ESM_DEFAULT_VAL';");
        });

        Assert.Contains("ESM_DEFAULT_VAL", output);
    }

    [Fact]
    public async Task Namespace_import_collects_named_exports()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import * as ns from './mod.js';\nexport const r = ns.a;");
            File.WriteAllText(Path.Combine(dir, "mod.js"), "export const a = 'NS_A_VAL';\nexport const b = 'NS_B_VAL';");
        });

        Assert.Contains("NS_A_VAL", output);
    }

    [Fact]
    public async Task Default_import_of_commonjs_module_exports()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import cfg from './cjs.js';\nexport const r = cfg;");
            File.WriteAllText(Path.Combine(dir, "cjs.js"), "module.exports = { name: 'CJS_DEFAULT_VAL' };");
        });

        Assert.Contains("CJS_DEFAULT_VAL", output);
    }
}
