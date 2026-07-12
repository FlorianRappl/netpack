namespace NetPack.Tests;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

public class MinifyImportTests
{
    [Fact]
    public async Task Named_import_key_survives_minification()
    {
        var output = await BundleMinified("app.js",
            ("lib.js", "export function zzUnique() { return 42; }"),
            ("app.js", "import { zzUnique } from './lib';\nexport const r = zzUnique;"));

        // The destructuring that reads the import must keep the export name as its
        // key (`{ zzUnique: <local> }`); the mangler must rename only the local
        // binding, not the property key it shares a node with.
        Assert.Contains("zzUnique:", output);
    }

    [Fact]
    public async Task Default_import_key_is_preserved_under_minification()
    {
        var output = await BundleMinified("app.js",
            ("lib.js", "export default function () { return 1; }"),
            ("app.js", "import lib from './lib';\nexport const r = lib;"));

        Assert.Contains("default:", output);
    }

    [Fact]
    public async Task Local_export_name_survives_minification()
    {
        var output = await BundleMinified("app.js",
            ("lib.js", "function widgetFn() { return 5; }\nexport { widgetFn };"),
            ("app.js", "import { widgetFn } from './lib';\nexport const r = widgetFn();"));

        // `export { widgetFn }` shares one Identifier for local and exported names;
        // the emitted `exports.widgetFn = …` key must not be mangled.
        Assert.Contains(".widgetFn", output);
    }

    [Fact]
    public async Task Lazy_import_chunk_is_valid_javascript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-lazy-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "Page.jsx"), "export default function Page() { return null; }");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.jsx"),
                "export const load = () => import('./Page');");

            using var graph = await Traverse.From(Path.Combine(dir, "app.jsx"));

            // Every emitted JS bundle — including the async chunk for `./Page` — must
            // be syntactically valid after minification.
            foreach (var bundle in graph.Context.Bundles.Values.OfType<JsBundle>())
            {
                var output = bundle.Stringify(new OutputOptions { IsOptimizing = true, IsReloading = false });
                var reparsed = Parser.ParseModule(output, "out.js",
                    new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
                Assert.Empty(reparsed.Diagnostics);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<string> BundleMinified(string entry, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-min-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            if (!files.Any(f => f.Name == "package.json"))
            {
                await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            }

            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, entry));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = true, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
