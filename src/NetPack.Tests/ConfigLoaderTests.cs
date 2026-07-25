namespace NetPack.Tests;

using NetPack.Graph;
using Xunit;

public class ConfigLoaderTests
{
    [Fact]
    public void ParseJson_parses_basic_config()
    {
        var json = """{"minify": true, "sourceMap": false, "format": "cjs"}""";
        var config = NetpackConfig.ParseJson(json);

        Assert.True(config.Minify);
        Assert.False(config.SourceMap);
        Assert.Equal("cjs", config.Format);
    }

    [Fact]
    public void ParseJson_parses_define()
    {
        var json = """{"define": {"__VERSION__": "'1.0.0'", "process.env.NODE_ENV": "'production'"}}""";
        var config = NetpackConfig.ParseJson(json);

        Assert.NotNull(config.Define);
        Assert.Equal("'1.0.0'", config.Define!["__VERSION__"]);
        Assert.Equal("'production'", config.Define!["process.env.NODE_ENV"]);
    }

    [Fact]
    public void ParseJson_parses_resolve_alias()
    {
        var json = """{"resolve": {"alias": {"@": "./src", "@components": "./src/components"}}}""";
        var config = NetpackConfig.ParseJson(json);

        Assert.NotNull(config.ResolveAlias);
        Assert.Equal(2, config.ResolveAlias!.Count);
        Assert.Equal("./src", config.ResolveAlias!["@"]);
        Assert.Equal("./src/components", config.ResolveAlias!["@components"]);
    }

    [Fact]
    public void ParseJson_parses_loader()
    {
        var json = """{"loader": {".svg": "text", ".png": "dataurl"}}""";
        var config = NetpackConfig.ParseJson(json);

        Assert.NotNull(config.Loader);
        Assert.Equal("text", config.Loader![".svg"]);
        Assert.Equal("dataurl", config.Loader![".png"]);
    }

    [Fact]
    public void ParseJson_parses_external()
    {
        var json = """{"external": ["react", "react-dom"]}""";
        var config = NetpackConfig.ParseJson(json);

        Assert.NotNull(config.External);
        Assert.Equal(2, config.External!.Count);
        Assert.Contains("react", config.External!);
        Assert.Contains("react-dom", config.External!);
    }

    [Fact]
    public void ParseJson_parses_all_fields()
    {
        var json = """
        {
            "mode": "production",
            "outDir": "build",
            "format": "esm",
            "platform": "node",
            "minify": true,
            "sourceMap": true,
            "publicPath": "/static/",
            "entryNames": "[name]-[hash]",
            "packages": "external",
            "preset": "production",
            "define": {"__VERSION__": "'1.0.0'"},
            "resolve": {"alias": {"@": "./src"}},
            "external": ["lodash"],
            "shared": ["react"],
            "conditions": ["development"]
        }
        """;
        var config = NetpackConfig.ParseJson(json);

        Assert.Equal("production", config.Mode);
        Assert.Equal("build", config.OutDir);
        Assert.Equal("esm", config.Format);
        Assert.Equal("node", config.Platform);
        Assert.True(config.Minify);
        Assert.True(config.SourceMap);
        Assert.Equal("/static/", config.PublicPath);
        Assert.Equal("[name]-[hash]", config.EntryNames);
        Assert.Equal("external", config.Packages);
        Assert.Equal("production", config.Preset);
        Assert.Single(config.Define!);
        Assert.Single(config.ResolveAlias!);
        Assert.Single(config.External!);
        Assert.Single(config.Shared!);
        Assert.Single(config.Conditions!);
    }

    [Fact]
    public void ParseJson_handles_empty_config()
    {
        var json = "{}";
        var config = NetpackConfig.ParseJson(json);

        Assert.Null(config.Mode);
        Assert.Null(config.OutDir);
        Assert.Null(config.Format);
        Assert.Null(config.Minify);
        Assert.Null(config.Define);
        Assert.Null(config.ResolveAlias);
        Assert.Null(config.External);
    }
}
