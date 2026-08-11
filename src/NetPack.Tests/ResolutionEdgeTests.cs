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
    public async Task Resolves_a_package_main_without_an_extension()
    {
        // Some packages set "main" without a file extension (e.g. "index"); the
        // resolver should fall back to extension probing when the bare path isn't
        // a file on its own.
        var output = await Bundle(dir =>
        {
            var pkg = Path.Combine(dir, "node_modules", "extless-main");
            Directory.CreateDirectory(pkg);
            File.WriteAllText(Path.Combine(pkg, "package.json"),
                "{\"name\":\"extless-main\",\"version\":\"1.0.0\",\"main\":\"index\"}");
            File.WriteAllText(Path.Combine(pkg, "index.js"), "export const a = 'EXTLESS_MAIN_MARKER';");
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { a } from 'extless-main';\nconsole.log(a);");
        });

        Assert.Contains("EXTLESS_MAIN_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Resolves_a_package_main_pointing_at_a_directory()
    {
        // "main" points at a directory; it should resolve that directory's index.
        var output = await Bundle(dir =>
        {
            var pkg = Path.Combine(dir, "node_modules", "dir-main");
            Directory.CreateDirectory(Path.Combine(pkg, "lib"));
            File.WriteAllText(Path.Combine(pkg, "package.json"),
                "{\"name\":\"dir-main\",\"version\":\"1.0.0\",\"main\":\"lib\"}");
            File.WriteAllText(Path.Combine(pkg, "lib", "index.js"), "export const a = 'DIRMAIN_MARKER';");
            File.WriteAllText(Path.Combine(dir, "main.js"), "import { a } from 'dir-main';\nconsole.log(a);");
        });

        Assert.Contains("DIRMAIN_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Resolves_a_tsconfig_paths_alias()
    {
        // `@/components` doesn't resolve normally; the tsconfig "paths" alias
        // `@/*` -> `./src/*` should map it to src/components.ts.
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "tsconfig.json"),
                "{ \"compilerOptions\": { \"paths\": { \"@/*\": [\"./src/*\"] } } }");
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            File.WriteAllText(Path.Combine(dir, "src", "components.ts"),
                "export const Foo = 'TSPATHS_MARKER';");
            File.WriteAllText(Path.Combine(dir, "main.ts"),
                "import { Foo } from '@/components';\nconsole.log(Foo);");
        }, "main.ts");

        Assert.Contains("TSPATHS_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Resolves_a_tsconfig_paths_alias_relative_to_baseurl()
    {
        // Targets are resolved relative to "baseUrl" (here ./app).
        var output = await Bundle(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "tsconfig.json"),
                "{ \"compilerOptions\": { \"baseUrl\": \"./app\", \"paths\": { \"@components/*\": [\"components/*\"] } } }");
            Directory.CreateDirectory(Path.Combine(dir, "app", "components"));
            File.WriteAllText(Path.Combine(dir, "app", "components", "button.ts"),
                "export const Button = 'BASEURL_MARKER';");
            File.WriteAllText(Path.Combine(dir, "main.ts"),
                "import { Button } from '@components/button';\nconsole.log(Button);");
        }, "main.ts");

        Assert.Contains("BASEURL_MARKER", output);
        AssertValid(output);
    }

    [Fact]
    public async Task Uses_the_closest_tsconfig_per_package_in_a_monorepo()
    {
        // Two packages each define their own `@/*` mapping; the same specifier must
        // resolve within whichever package the importing file lives in.
        var output = await Bundle(dir =>
        {
            foreach (var (pkg, marker) in new[] { ("a", "PKG_A_MARKER"), ("b", "PKG_B_MARKER") })
            {
                var root = Path.Combine(dir, "packages", pkg);
                Directory.CreateDirectory(Path.Combine(root, "src"));
                File.WriteAllText(Path.Combine(root, "tsconfig.json"),
                    "{ \"compilerOptions\": { \"paths\": { \"@/*\": [\"./src/*\"] } } }");
                File.WriteAllText(Path.Combine(root, "index.ts"),
                    "import { X } from '@/thing';\nexport default X;");
                File.WriteAllText(Path.Combine(root, "src", "thing.ts"),
                    $"export const X = '{marker}';");
            }

            File.WriteAllText(Path.Combine(dir, "main.ts"),
                "import a from './packages/a/index';\nimport b from './packages/b/index';\nconsole.log(a, b);");
        }, "main.ts");

        Assert.Contains("PKG_A_MARKER", output);
        Assert.Contains("PKG_B_MARKER", output);
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
