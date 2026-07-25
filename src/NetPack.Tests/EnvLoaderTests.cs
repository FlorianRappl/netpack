namespace NetPack.Tests;

using System.IO;
using System.Threading.Tasks;
using NetPack.Graph;
using Xunit;

public class EnvLoaderTests
{
    [Fact]
    public void Parse_loads_key_value_pairs()
    {
        var content = "VITE_API_URL=https://api.example.com\nVITE_APP_TITLE=My App";
        var env = EnvLoader.Parse(content);
        Assert.Equal("https://api.example.com", env["VITE_API_URL"]);
        Assert.Equal("My App", env["VITE_APP_TITLE"]);
    }

    [Fact]
    public void Parse_filters_by_prefix()
    {
        var content = "VITE_API_URL=https://api.example.com\nSECRET_KEY=abc123";
        var env = EnvLoader.Parse(content, prefix: "VITE_");
        Assert.Single(env);
        Assert.True(env.ContainsKey("VITE_API_URL"));
    }

    [Fact]
    public void Parse_handles_double_quoted_values()
    {
        var content = "VITE_TITLE=\"My App\"";
        var env = EnvLoader.Parse(content);
        Assert.Equal("My App", env["VITE_TITLE"]);
    }

    [Fact]
    public void Parse_handles_single_quoted_values()
    {
        var content = "VITE_URL='https://example.com'";
        var env = EnvLoader.Parse(content);
        Assert.Equal("https://example.com", env["VITE_URL"]);
    }

    [Fact]
    public void Parse_ignores_comments()
    {
        var content = "# This is a comment\nVITE_API_URL=https://api.example.com\n# Another comment";
        var env = EnvLoader.Parse(content);
        Assert.Single(env);
    }

    [Fact]
    public void Parse_ignores_empty_lines()
    {
        var content = "\n\nVITE_API_URL=https://api.example.com\n\n";
        var env = EnvLoader.Parse(content);
        Assert.Single(env);
    }

    [Fact]
    public void Parse_returns_empty_for_empty_content()
    {
        var env = EnvLoader.Parse("");
        Assert.Empty(env);
    }

    [Fact]
    public void Parse_handles_values_with_equals_sign()
    {
        var content = "VITE_CONFIG=key=value=extra";
        var env = EnvLoader.Parse(content);
        Assert.Equal("key=value=extra", env["VITE_CONFIG"]);
    }

    [Fact]
    public async Task LoadFromDirectory_loads_in_priority_order()
    {
        var dir = Path.Combine(Path.GetTempPath(), "env-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, ".env"), "VITE_BASE=base\nVITE_OVERRIDE=base");
            await File.WriteAllTextAsync(Path.Combine(dir, ".env.local"), "VITE_LOCAL=local\nVITE_OVERRIDE=local");

            var env = EnvLoader.LoadFromDirectory(dir, prefix: "VITE_");
            Assert.Equal("base", env["VITE_BASE"]);
            Assert.Equal("local", env["VITE_LOCAL"]);
            Assert.Equal("local", env["VITE_OVERRIDE"]); // .env.local overrides .env
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadFromDirectory_loads_mode_specific_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "env-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, ".env"), "VITE_MODE=production");
            await File.WriteAllTextAsync(Path.Combine(dir, ".env.development"), "VITE_MODE=development");

            var env = EnvLoader.LoadFromDirectory(dir, mode: "development", prefix: "VITE_");
            Assert.Equal("development", env["VITE_MODE"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateReplacements_creates_import_meta_env_mappings()
    {
        var env = new Dictionary<string, string>
        {
            ["API_URL"] = "'https://api.example.com'",
            ["MODE"] = "'development'"
        };
        var result = EnvLoader.GenerateReplacements(env);
        Assert.Contains("import.meta.env.API_URL='https://api.example.com'", result);
        Assert.Contains("import.meta.env.MODE='development'", result);
    }
}
