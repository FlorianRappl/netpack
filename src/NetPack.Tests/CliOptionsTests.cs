namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Graph.Writers;
using Xunit;

public class CliOptionsTests
{
    private static async Task<string> BundleDir(
        string entryRelative,
        Action<string> writeFiles,
        IReadOnlyDictionary<string, string>? defines = null,
        IReadOnlyDictionary<string, string>? aliases = null,
        IReadOnlyDictionary<string, string>? loaders = null,
        IReadOnlyDictionary<string, string>? envVars = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            writeFiles(dir);

            using var graph = await Traverse.From(
                Path.Combine(dir, entryRelative), Array.Empty<string>(), Array.Empty<string>(),
                defines: defines, aliases: aliases, loaders: loaders, envVars: envVars);
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Define_replaces_the_builtin_node_env_by_default()
    {
        var output = await BundleDir("main.js", dir =>
            File.WriteAllText(Path.Combine(dir, "main.js"), "export const mode = process.env.NODE_ENV;"));

        // The printer normalizes string literals to double quotes.
        Assert.Contains("\"production\"", output);
        Assert.DoesNotContain("process.env.NODE_ENV", output);
    }

    [Fact]
    public async Task Define_substitutes_custom_constants()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"), "export const answer = __ANSWER__;"),
            defines: new Dictionary<string, string> { ["__ANSWER__"] = "42" });

        Assert.Contains("42", output);
        Assert.DoesNotContain("__ANSWER__", output);
    }

    [Fact]
    public async Task Define_can_override_the_builtin_node_env()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"), "export const mode = process.env.NODE_ENV;"),
            defines: new Dictionary<string, string> { ["process.env.NODE_ENV"] = "'test'" });

        Assert.Contains("\"test\"", output);
        Assert.DoesNotContain("\"production\"", output);
    }

    [Fact]
    public async Task Define_replaces_multiple_identifiers()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"),
                "export const a = __A__;\nexport const b = __B__;"),
            defines: new Dictionary<string, string>
            {
                ["__A__"] = "1",
                ["__B__"] = "2"
            });

        Assert.Contains("1", output);
        Assert.Contains("2", output);
        Assert.DoesNotContain("__A__", output);
        Assert.DoesNotContain("__B__", output);
    }

    [Fact]
    public async Task Define_with_string_value_preserves_quotes()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"),
                "export const name = __NAME__;"),
            defines: new Dictionary<string, string>
            {
                ["__NAME__"] = "'netpack'"
            });

        Assert.Contains("netpack", output);
    }

    [Fact]
    public async Task Define_does_not_replace_non_matching_keys()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"),
                "export const x = __MISSING__;"),
            defines: new Dictionary<string, string>
            {
                ["__VERSION"] = "'partial'"
            });

        Assert.DoesNotContain("partial", output);
        Assert.Contains("__MISSING__", output);
    }

    [Fact]
    public async Task Alias_chain_resolves_correctly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { x } from '@utils/helper';\nexport default x;");
            Directory.CreateDirectory(Path.Combine(dir, "src", "utils"));
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "utils", "helper.js"), "export const x = 'HELPER';");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(),
                aliases: new Dictionary<string, string>
                {
                    ["@utils/helper"] = Path.Combine(dir, "src", "utils", "helper.js")
                });
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            // Aliases resolve the module at graph level; the content is bundled
            Assert.Contains("HELPER", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportMetaEnv_replaces_with_env_values()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"),
                "export const url = import.meta.env.VITE_API_URL;"),
            envVars: new Dictionary<string, string>
            {
                ["VITE_API_URL"] = "'https://api.example.com'"
            });

        Assert.Contains("https://api.example.com", output);
        Assert.DoesNotContain("import.meta.env", output);
    }

    [Fact]
    public async Task ImportMetaEnv_replaces_MODE()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"),
                "export const mode = import.meta.env.MODE;"),
            envVars: new Dictionary<string, string>
            {
                ["MODE"] = "'development'"
            });

        Assert.Contains("development", output);
    }

    [Fact]
    public async Task ImportMetaEnv_leaves_unknown_vars_unchanged()
    {
        var output = await BundleDir("main.js",
            dir => File.WriteAllText(Path.Combine(dir, "main.js"),
                "export const x = import.meta.env.UNKNOWN_VAR;"),
            envVars: new Dictionary<string, string>());

        Assert.Contains("import.meta.env.UNKNOWN_VAR", output);
    }

    [Fact]
    public async Task Alias_target_is_bundled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { x } from 'virtual-lib';\nexport default x;");
            await File.WriteAllTextAsync(Path.Combine(dir, "real.js"), "export const x = 'ALIASED_VALUE';");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>(),
                aliases: new Dictionary<string, string> { ["virtual-lib"] = Path.Combine(dir, "real.js") });
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("ALIASED_VALUE", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Loader_text_inlines_a_file_as_a_string()
    {
        var output = await BundleDir("main.js", dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import logo from './logo.svg';\nexport default logo;");
            File.WriteAllText(Path.Combine(dir, "logo.svg"), "<svg>MARKER_TEXT</svg>");
        },
        loaders: new Dictionary<string, string> { [".svg"] = "text" });

        Assert.Contains("MARKER_TEXT", output);
    }

    [Fact]
    public async Task Loader_dataurl_inlines_a_base64_data_uri()
    {
        var output = await BundleDir("main.js", dir =>
        {
            File.WriteAllText(Path.Combine(dir, "main.js"), "import logo from './logo.svg';\nexport default logo;");
            File.WriteAllText(Path.Combine(dir, "logo.svg"), "<svg></svg>");
        },
        loaders: new Dictionary<string, string> { [".svg"] = "dataurl" });

        Assert.Contains("data:image/svg+xml;base64,", output);
    }

    [Fact]
    public async Task Entry_names_without_hash_keep_the_plain_name()
    {
        var (emitted, _) = await Emit("[name]");
        Assert.Contains(emitted, f => f.Name == "main.js");
    }

    [Fact]
    public async Task Entry_names_with_hash_produce_a_content_hashed_file()
    {
        var (emitted, _) = await Emit("[name]-[hash]");
        Assert.Contains(emitted, f => Regex.IsMatch(f.Name, @"^main-[0-9a-f]{6}\.js$"));
        Assert.DoesNotContain(emitted, f => f.Name == "main.js");
    }

    [Fact]
    public async Task Hashed_entry_name_is_deterministic_for_identical_content()
    {
        var (first, _) = await Emit("[name]-[hash]");
        var (second, _) = await Emit("[name]-[hash]");
        var a = first.Single(f => f.Name.EndsWith(".js")).Name;
        var b = second.Single(f => f.Name.EndsWith(".js")).Name;
        Assert.Equal(a, b);
    }

    private static async Task<(IReadOnlyList<EmittedFile> Emitted, string Dir)> Emit(string entryNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), "export default 21 * 2;");

        using var graph = await Traverse.From(Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
        var writer = new MemoryResultWriter(graph.Context);
        var emitted = await writer.WriteOut(new OutputOptions
        {
            IsOptimizing = false,
            IsReloading = false,
            EntryNames = entryNames,
        });

        Directory.Delete(dir, recursive: true);
        return (emitted, dir);
    }
}
