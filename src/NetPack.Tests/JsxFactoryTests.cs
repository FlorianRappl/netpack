namespace NetPack.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

public class JsxFactoryTests
{
    // -- JsxPragma.Scan (leading-comment pragma detection) -----------------

    [Fact]
    public void Scan_finds_jsx_pragma_in_block_comment()
    {
        var result = JsxPragma.Scan("/** @jsx h */\nimport x from 'y';");
        Assert.Equal("h", result.Factory);
        Assert.Null(result.FragmentFactory);
    }

    [Fact]
    public void Scan_finds_jsx_pragma_in_line_comment()
    {
        var result = JsxPragma.Scan("// @jsx h\nconst a = 1;");
        Assert.Equal("h", result.Factory);
    }

    [Fact]
    public void Scan_supports_dotted_factory()
    {
        var result = JsxPragma.Scan("/* @jsx Preact.h */");
        Assert.Equal("Preact.h", result.Factory);
    }

    [Fact]
    public void Scan_finds_both_jsx_and_jsxFrag_without_collision()
    {
        var result = JsxPragma.Scan("/** @jsx h */\n/** @jsxFrag Fragment */\ncode();");
        Assert.Equal("h", result.Factory);
        Assert.Equal("Fragment", result.FragmentFactory);
    }

    [Fact]
    public void Scan_does_not_treat_jsxFrag_as_jsx()
    {
        // Only @jsxFrag is present; @jsx must not match its prefix.
        var result = JsxPragma.Scan("/** @jsxFrag Fragment */");
        Assert.Null(result.Factory);
        Assert.Equal("Fragment", result.FragmentFactory);
    }

    [Fact]
    public void Scan_ignores_pragma_after_code()
    {
        // The pragma only counts before any code has been written.
        var result = JsxPragma.Scan("const a = 1;\n/** @jsx h */");
        Assert.Null(result.Factory);
    }

    [Fact]
    public void Scan_returns_nothing_when_absent()
    {
        var result = JsxPragma.Scan("import React from 'react';\nconst a = <div />;");
        Assert.Null(result.Factory);
        Assert.Null(result.FragmentFactory);
    }

    // -- End-to-end lowering through the bundler ---------------------------

    [Fact]
    public async Task Default_factory_is_react_create_element()
    {
        var output = await Bundle("app.jsx", ("app.jsx", "export const a = <div />;"));
        Assert.Contains("React.createElement(\"div\"", output);
    }

    [Fact]
    public async Task Static_children_are_passed_variadically_not_as_an_array()
    {
        var output = await Bundle("app.jsx",
            ("app.jsx", "export const a = <ul><li>one</li><li>two</li></ul>;"));

        Assert.Contains("React.createElement(\"ul\"", output);
        // Children must be separate trailing args, not a single array argument
        // (an array child makes React demand `key` props and warn).
        Assert.DoesNotContain("React.createElement(\"ul\", null, [", output);
        Assert.DoesNotContain("[React.createElement", output);
    }

    [Fact]
    public async Task Local_jsx_pragma_overrides_factory()
    {
        var output = await Bundle("app.jsx", ("app.jsx", "/** @jsx h */\nexport const a = <div />;"));
        Assert.Contains("h(\"div\"", output);
        Assert.DoesNotContain("React.createElement", output);
    }

    [Fact]
    public async Task Local_jsxFrag_pragma_overrides_fragment()
    {
        var output = await Bundle("app.jsx",
            ("app.jsx", "/** @jsx h */\n/** @jsxFrag Fragment */\nexport const a = <>{1}</>;"));
        Assert.Contains("h(Fragment", output);
    }

    [Fact]
    public async Task TsConfig_factory_applies_to_typescript_files()
    {
        var output = await Bundle("app.tsx",
            ("tsconfig.json", "{ \"compilerOptions\": { \"jsxFactory\": \"h\" } }"),
            ("app.tsx", "export const a = <div />;"));
        Assert.Contains("h(\"div\"", output);
        Assert.DoesNotContain("React.createElement", output);
    }

    [Fact]
    public async Task TsConfig_factory_ignored_for_plain_js_files()
    {
        // tsconfig's jsxFactory only applies to TS sources; a .jsx file keeps the default.
        var output = await Bundle("app.jsx",
            ("tsconfig.json", "{ \"compilerOptions\": { \"jsxFactory\": \"h\" } }"),
            ("app.jsx", "export const a = <div />;"));
        Assert.Contains("React.createElement(\"div\"", output);
    }

    [Fact]
    public async Task Local_pragma_wins_over_tsconfig()
    {
        var output = await Bundle("app.tsx",
            ("tsconfig.json", "{ \"compilerOptions\": { \"jsxFactory\": \"h\" } }"),
            ("app.tsx", "/** @jsx dom */\nexport const a = <div />;"));
        Assert.Contains("dom(\"div\"", output);
    }

    [Fact]
    public async Task Preact_dependency_without_react_uses_preact_factory_and_import()
    {
        var output = await Bundle("app.jsx",
            ("package.json", "{ \"dependencies\": { \"preact\": \"^10.0.0\" } }"),
            ("app.jsx", "export const a = <div />;"));

        Assert.Contains("import Preact from \"preact\";", output);
        Assert.Contains("Preact.h(\"div\"", output);
        Assert.DoesNotContain("React.createElement", output);
    }

    [Fact]
    public async Task React_dependency_keeps_react_default()
    {
        var output = await Bundle("app.jsx",
            ("package.json", "{ \"dependencies\": { \"preact\": \"^10.0.0\", \"react\": \"^18.0.0\" } }"),
            ("app.jsx", "export const a = <div />;"));

        Assert.Contains("React.createElement(\"div\"", output);
        Assert.DoesNotContain("Preact.h", output);
    }

    // Writes the given files to a fresh temp directory (always including a
    // package.json so root resolution is hermetic), bundles the entry, and
    // returns the pretty-printed JS.
    private static async Task<string> Bundle(string entry, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-jsx-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            var provided = files.Select(f => f.Name).ToHashSet();
            if (!provided.Contains("package.json"))
            {
                await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            }

            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            }

            using var graph = await Traverse.From(Path.Combine(dir, entry));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
