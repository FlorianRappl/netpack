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
/// End-to-end resolution and linking edge cases. Each writes a small project to a
/// temp dir, bundles it, and asserts the expected module made it in and the whole
/// bundle is valid JavaScript (catching malformed-output regressions cheaply).
/// </summary>
public class ResolutionEdgeTests
{
    private static async Task<string> Bundle(Action<string> setup, string entry = "main.js")
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-res-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            setup(dir);

            using var graph = await Traverse.From(Path.Combine(dir, entry), Array.Empty<string>(), Array.Empty<string>());
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertValid(string js)
        => Assert.Empty(Parser.ParseModule(js, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);

    [Fact]
    public async Task Resolves_an_extensionless_relative_import()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { a } from './util';\nconsole.log(a);");
            File.WriteAllText(Path.Combine(dir, "util.js"), "export const a = 'EXTLESS_MARKER';");
        });

        Assert.Contains("EXTLESS_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Resolves_a_directory_index()
    {
        var output = await Bundle(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib"));
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { a } from './lib';\nconsole.log(a);");
            File.WriteAllText(Path.Combine(dir, "lib", "index.js"), "export const a = 'DIRINDEX_MARKER';");
        });

        Assert.Contains("DIRINDEX_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Prefers_a_typescript_extension_when_resolving()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { a } from './mod';\nconsole.log(a);");
            File.WriteAllText(Path.Combine(dir, "mod.ts"), "export const a: string = 'TSMOD_MARKER';");
        });

        Assert.Contains("TSMOD_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Imports_json_as_a_module()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import data from './data.json';\nconsole.log(data);");
            File.WriteAllText(Path.Combine(dir, "data.json"), "{ \"key\": \"JSONVAL_MARKER\" }");
        });

        Assert.Contains("JSONVAL_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Handles_circular_dependencies()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"),
                "import { a } from './a';\nimport { b } from './b';\nconsole.log(a, b);");
            File.WriteAllText(Path.Combine(dir, "a.js"),
                "import { b } from './b';\nexport const a = 'CIRC_A';\nexport function useB() { return b; }");
            File.WriteAllText(Path.Combine(dir, "b.js"),
                "import { a } from './a';\nexport const b = 'CIRC_B';\nexport function useA() { return a; }");
        });

        Assert.Contains("CIRC_A", output);
        Assert.Contains("CIRC_B", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Follows_a_re_export_chain()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { x } from './index.js';\nconsole.log(x);");
            File.WriteAllText(Path.Combine(dir, "index.js"), "export { x } from './a';");
            File.WriteAllText(Path.Combine(dir, "a.js"), "export const x = 'REEXPORT_MARKER';");
        });

        Assert.Contains("REEXPORT_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Resolves_a_parent_directory_import()
    {
        var output = await Bundle(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            Directory.CreateDirectory(Path.Combine(dir, "shared"));
            File.WriteAllText(Path.Combine(dir, "src", "main.js"), "import { s } from '../shared/mod.js';\nconsole.log(s);");
            File.WriteAllText(Path.Combine(dir, "shared", "mod.js"), "export const s = 'PARENTREL_MARKER';");
        }, entry: Path.Combine("src", "main.js"));

        Assert.Contains("PARENTREL_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Bundles_a_commonjs_module_via_interop()
    {
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import m from './cjs.js';\nconsole.log(m);");
            File.WriteAllText(Path.Combine(dir, "cjs.js"), "module.exports = 'CJSVAL_MARKER';");
        });

        Assert.Contains("CJSVAL_MARKER", output);
        AssertValid(output);
    }
}
