namespace NetPack.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using Xunit;

public class ModuleFormatTests
{
    [Theory]
    [InlineData(ModuleFormat.Esm, typeof(EsmModuleFormat))]
    [InlineData(ModuleFormat.CommonJs, typeof(CommonJsModuleFormat))]
    [InlineData(ModuleFormat.Umd, typeof(UmdModuleFormat))]
    [InlineData(ModuleFormat.SystemJs, typeof(SystemJsModuleFormat))]
    public void Factory_returns_the_matching_format(ModuleFormat format, Type expected)
    {
        Assert.IsType(expected, JsModuleFormats.For(format));
    }

    [Theory]
    [InlineData(ModuleFormat.Esm, "export default")]
    [InlineData(ModuleFormat.CommonJs, "module.exports")]
    [InlineData(ModuleFormat.Umd, "define.amd")]
    [InlineData(ModuleFormat.SystemJs, "System.register")]
    public async Task Each_format_bundles_to_valid_js(ModuleFormat format, string marker)
    {
        var output = await Bundle(format, "export const value = 1 + 2;\nexport default value;");

        Assert.Contains(marker, output);
        Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
    }

    [Theory]
    [InlineData(ModuleFormat.CommonJs, "require(\"react\")")]
    [InlineData(ModuleFormat.Umd, "define([\"react\"], factory)")]
    [InlineData(ModuleFormat.SystemJs, "System.register([\"react\"]")]
    public async Task External_dependencies_are_linked_per_format(ModuleFormat format, string marker)
    {
        // `react` is external, so it stays a bare dependency the envelope wires up.
        var output = await Bundle(format, "import React from 'react';\nexport default React;", externals: "react");

        Assert.Contains(marker, output);
        Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
    }

    private static async Task<string> Bundle(ModuleFormat format, string entry, string? externals = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-fmt-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"), entry);

            var external = externals is null ? Array.Empty<string>() : new[] { externals };
            using var graph = await Traverse.From(Path.Combine(dir, "main.js"), external, Array.Empty<string>());
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);

            return bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false, Format = format });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
