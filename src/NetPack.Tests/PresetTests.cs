namespace NetPack.Tests;

using System.IO;
using System.Linq;
using NetPack.Config;
using Xunit;

public class PresetTests
{
    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-preset-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Options_resolve_first_write_wins()
    {
        var dir = Dir();

        try
        {
            Write(dir, "base.json", "{ \"minify\": true, \"banner\": \"base\" }");
            // JSONC: comments + trailing comma tolerated.
            Write(dir, "child.json", "{ \"banner\": \"child\", /* mine wins */ \"presets\": [\"./base.json\"], }");

            var resolved = Presets.Resolve([Path.Combine(dir, "child.json")], dir);

            Assert.Equal("child", resolved.Options.Banner);   // child is higher priority
            Assert.True(resolved.Options.Minify);              // inherited from base
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Entry_reference_order_sets_priority()
    {
        var dir = Dir();

        try
        {
            Write(dir, "a.json", "{ \"banner\": \"a\" }");
            Write(dir, "b.json", "{ \"banner\": \"b\", \"platform\": \"node\" }");

            // a listed first → a wins conflicts, b fills the gaps.
            var resolved = Presets.Resolve([Path.Combine(dir, "a.json"), Path.Combine(dir, "b.json")], dir);

            Assert.Equal("a", resolved.Options.Banner);
            Assert.Equal("node", resolved.Options.Platform);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reference_cycles_are_safe()
    {
        var dir = Dir();

        try
        {
            Write(dir, "a.json", "{ \"banner\": \"a\", \"presets\": [\"./b.json\"] }");
            Write(dir, "b.json", "{ \"minify\": true, \"presets\": [\"./a.json\"] }");

            var resolved = Presets.Resolve([Path.Combine(dir, "a.json")], dir);

            Assert.Equal("a", resolved.Options.Banner);
            Assert.True(resolved.Options.Minify);
            Assert.Equal(2, resolved.Sources.Count); // each loaded exactly once
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Diamond_references_load_each_preset_once()
    {
        var dir = Dir();

        try
        {
            Write(dir, "d.json", "{ \"banner\": \"d\" }");
            Write(dir, "b.json", "{ \"presets\": [\"./d.json\"] }");
            Write(dir, "c.json", "{ \"presets\": [\"./d.json\"] }");
            Write(dir, "a.json", "{ \"presets\": [\"./b.json\", \"./c.json\"] }");

            var resolved = Presets.Resolve([Path.Combine(dir, "a.json")], dir);

            Assert.Equal("d", resolved.Options.Banner);
            Assert.Equal(4, resolved.Sources.Count); // a, b, d, c — d only once
            Assert.Single(resolved.Sources, s => Path.GetFileName(s) == "d.json");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Hooks_merge_base_first_and_dedupe()
    {
        var dir = Dir();

        try
        {
            Write(dir, "baseHook.js", "export default () => {};");
            Write(dir, "entryHook.js", "export default () => {};");
            Write(dir, "shared.js", "export default () => {};");

            Write(dir, "base.json",
                "{ \"hooks\": { \"afterBundling\": [\"./baseHook.js\", \"./shared.js\"] } }");
            Write(dir, "entry.json",
                "{ \"presets\": [\"./base.json\"], \"hooks\": { \"afterBundling\": [\"./shared.js\", \"./entryHook.js\"] } }");

            var resolved = Presets.Resolve([Path.Combine(dir, "entry.json")], dir);
            var hook = resolved.Hooks["afterBundling"].Select(Path.GetFileName).ToList();

            // Base preset runs first; the shared module (reached from both) appears
            // once, keeping its earliest (base) position.
            Assert.Equal(new[] { "baseHook.js", "shared.js", "entryHook.js" }, hook);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_package_preset_resolves_via_its_main_field()
    {
        var dir = Dir();

        try
        {
            Write(dir, "node_modules/@acme/base/package.json",
                "{ \"name\": \"@acme/base\", \"main\": \"preset.json\" }");
            Write(dir, "node_modules/@acme/base/preset.json", "{ \"banner\": \"from-package\" }");
            Write(dir, "entry.json", "{ \"presets\": [\"@acme/base\"] }");

            var resolved = Presets.Resolve([Path.Combine(dir, "entry.json")], dir);

            Assert.Equal("from-package", resolved.Options.Banner);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Variants_are_parsed_from_preset_json()
    {
        var dir = Dir();

        try
        {
            Write(dir, "netpack.json", @"{
                ""outdir"": ""dist"",
                ""variants"": {
                    ""web"": { ""platform"": ""web"", ""minify"": true },
                    ""node"": { ""platform"": ""node"" }
                }
            }");

            var resolved = Presets.Resolve([Path.Combine(dir, "netpack.json")], dir);

            Assert.NotNull(resolved.Options.Variants);
            Assert.Equal(2, resolved.Options.Variants!.Count);
            Assert.True(resolved.Options.Variants!.ContainsKey("web"));
            Assert.True(resolved.Options.Variants!.ContainsKey("node"));
            Assert.Equal("web", resolved.Options.Variants!["web"].Platform);
            Assert.Equal("node", resolved.Options.Variants!["node"].Platform);
            Assert.True(resolved.Options.Variants!["web"].Minify);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
