namespace NetPack.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Json;
using Xunit;

/// <summary>
/// Build metafile JSON: artifact and dependency manifest for tooling and analysis.
/// </summary>
public class MetafileTests
{
    private static async Task<MetadataContainer> BuildAndGetMetafile(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-meta-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            if (!files.Any(f => f.Name == "package.json"))
            {
                await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            }

            foreach (var (name, content) in files)
            {
                var fullPath = Path.Combine(dir, name);
                var subDir = Path.GetDirectoryName(fullPath);
                if (subDir is not null && !Directory.Exists(subDir))
                    Directory.CreateDirectory(subDir);
                await File.WriteAllTextAsync(fullPath, content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, "index.html"));

            // Create emitted files from the bundle/asset data to verify metafile generation
            var emitted = new List<EmittedFile>();
            foreach (var bundle in graph.Context.Bundles.Values)
            {
                emitted.Add(new EmittedFile(bundle.GetFileName(), bundle.Root.Bytes, bundle.Items.Length, IsBundle: true));
            }
            foreach (var asset in graph.Context.Assets.Values)
            {
                emitted.Add(new EmittedFile(asset.GetFileName(), asset.Content.Length, Modules: 0, IsBundle: false));
            }

            var json = Traverse.BuildMetafile(graph.Context, emitted);
            return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.MetadataContainer)!;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 1: Metafile generated with inputs and outputs ------------------

    [Fact]
    public async Task Metafile_contains_inputs_and_outputs()
    {
        var container = await BuildAndGetMetafile(
            ("index.html", "<!doctype html><html><head><script type=\"module\" src=\"./app.js\"></script></head><body></body></html>"),
            ("app.js", "import { helper } from './helper.js';\nexport const x = helper();"),
            ("helper.js", "export function helper() { return 1; }"));

        Assert.NotNull(container.Inputs);
        Assert.NotNull(container.Outputs);

        // Should have at least app.js and helper.js as inputs
        Assert.True(container.Inputs!.Count >= 2);

        // Should have at least the JS bundle as output
        Assert.True(container.Outputs!.Count >= 1);
    }

    // -- Test 2: Entry point flagged correctly -------------------------------

    [Fact]
    public async Task Entry_point_has_correct_flags()
    {
        // Use a JS entry point directly so the bundle is the entry
        var dir = Path.Combine(Path.GetTempPath(), "netpack-meta-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "export const x = 1;");

            using var graph = await Traverse.From(Path.Combine(dir, "app.js"));
            var emitted = graph.Context.Bundles.Values
                .Select(b => new EmittedFile(b.GetFileName(), b.Root.Bytes, b.Items.Length, IsBundle: true))
                .ToList();
            var json = Traverse.BuildMetafile(graph.Context, emitted);
            var container = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.MetadataContainer)!;

            // The entry bundle should have entryPoint set and flags = "entry"
            var entryOutput = container.Outputs!
                .FirstOrDefault(o => o.Value.Flags == "entry");

            Assert.NotEqual(default, entryOutput);
            Assert.NotNull(entryOutput.Value.EntryPoint);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 3: Byte counts in metafile -------------------------------------

    [Fact]
    public async Task Metafile_contains_byte_counts()
    {
        var container = await BuildAndGetMetafile(
            ("index.html", "<!doctype html><html><head><script type=\"module\" src=\"./app.js\"></script></head><body></body></html>"),
            ("app.js", "export const x = 1;"));

        foreach (var (_, input) in container.Inputs!)
        {
            Assert.True(input.Bytes >= 0);
        }

        foreach (var (_, output) in container.Outputs!)
        {
            Assert.True(output.Bytes >= 0);
        }
    }

    // -- Test 4: Deterministic across builds ---------------------------------

    [Fact]
    public async Task Metafile_is_deterministic_across_builds()
    {
        // Use a stable path to avoid temp dir randomness in the comparison
        var dir = Path.Combine(Path.GetTempPath(), "netpack-meta-stable-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "export const x = 1;");

            using var graph1 = await Traverse.From(Path.Combine(dir, "app.js"));
            var emitted1 = graph1.Context.Bundles.Values
                .Select(b => new EmittedFile(b.GetFileName(), b.Root.Bytes, b.Items.Length, IsBundle: true))
                .ToList();
            var json1 = Traverse.BuildMetafile(graph1.Context, emitted1);

            using var graph2 = await Traverse.From(Path.Combine(dir, "app.js"));
            var emitted2 = graph2.Context.Bundles.Values
                .Select(b => new EmittedFile(b.GetFileName(), b.Root.Bytes, b.Items.Length, IsBundle: true))
                .ToList();
            var json2 = Traverse.BuildMetafile(graph2.Context, emitted2);

            Assert.Equal(json1, json2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 5: Shared CSS chunk appears in outputs -------------------------

    [Fact]
    public async Task Metafile_includes_shared_css_chunks()
    {
        var container = await BuildAndGetMetafile(
            ("index.html",
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>"),
            ("shared.css", ".common { color: red; }"),
            ("app1.js", "import './shared.css';\nexport const a = 1;"),
            ("app2.js", "import './shared.css';\nexport const b = 2;"));

        // Shared CSS should appear as a non-entry output
        var cssOutputs = container.Outputs!
            .Where(o => o.Key.EndsWith(".css", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(cssOutputs);
    }

    // -- Test 6: No metafile when disabled ------------------------------------

    [Fact]
    public async Task No_metafile_written_when_path_not_set()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-meta-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.js"), "export const x = 1;");

            // Build without metafile path — no file should be written
            var outputDir = Path.Combine(dir, "dist");
            var options = new BundleOptions();
            await Bundler.WriteToDirectoryAsync(Path.Combine(dir, "app.js"), outputDir, options);

            var metaFiles = Directory.GetFiles(outputDir, "*.json");
            Assert.Empty(metaFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -- Test 7: Dependency edges include original import specifiers ----------

    [Fact]
    public async Task Input_imports_include_original_specifier()
    {
        var container = await BuildAndGetMetafile(
            ("index.html", "<!doctype html><html><head><script type=\"module\" src=\"./app.js\"></script></head><body></body></html>"),
            ("app.js", "import { helper } from './helper.js';\nexport const x = helper();"),
            ("helper.js", "export function helper() { return 1; }"));

        var appInput = container.Inputs!.First(i => i.Key.EndsWith("app.js"));

        // Should have at least one import with the correct specifier
        Assert.Contains(appInput.Value.Imports!, i => i.Original == "./helper.js");
        Assert.Equal("import-statement", appInput.Value.Imports![0].Kind);
    }

    // -- Test 8: CSS imported from JS appears in metafile --------------------

    [Fact]
    public async Task Css_imported_from_js_appears_in_metafile()
    {
        var container = await BuildAndGetMetafile(
            ("index.html", "<!doctype html><html><head><script type=\"module\" src=\"./app.js\"></script></head><body></body></html>"),
            ("app.js", "import './style.css';\nexport const x = 1;"),
            ("style.css", ".x { color: red; }"));

        // The app.js input should reference style.css as a dependency
        var appInput = container.Inputs!.First(i => i.Key.EndsWith("app.js"));
        Assert.Contains(appInput.Value.Imports!, i => i.Original == "./style.css");
    }

    // -- Test 9: Non-entry output has null entryPoint ------------------------

    [Fact]
    public async Task Non_entry_output_has_null_entry_point()
    {
        var container = await BuildAndGetMetafile(
            ("index.html",
                "<!doctype html><html><head>" +
                "<script type=\"module\" src=\"./app1.js\"></script>" +
                "<script type=\"module\" src=\"./app2.js\"></script>" +
                "</head><body></body></html>"),
            ("shared.js", "export const common = 1;"),
            ("app1.js", "import { common } from './shared.js';\nexport const a = common;"),
            ("app2.js", "import { common } from './shared.js';\nexport const b = common;"));

        // The shared chunk should have flags=shared and entryPoint=null
        var sharedOutputs = container.Outputs!
            .Where(o => o.Value.Flags == "shared")
            .ToList();

        Assert.NotEmpty(sharedOutputs);
        foreach (var (_, output) in sharedOutputs)
        {
            Assert.Null(output.EntryPoint);
            Assert.Equal("shared", output.Flags);
        }
    }
}
