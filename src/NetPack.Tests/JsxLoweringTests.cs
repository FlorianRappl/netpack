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
/// Verifies the shape of lowered JSX: intrinsic vs. component callees, member
/// components, expression children, and props objects — the details that decide
/// whether the runtime factory is called correctly.
/// </summary>
public class JsxLoweringTests
{
    private static async Task<string> Lower(string jsx)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-jsxl-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "app.jsx"), jsx);

            using var graph = await Traverse.From(Path.Combine(dir, "app.jsx"));
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });
            Assert.Empty(Parser.ParseModule(output, "out.js", new ParserOptions { Tolerant = true }).Diagnostics);
            return output;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Intrinsic_elements_lower_to_a_string_tag()
    {
        var output = await Lower("export const a = <div />;");
        Assert.Contains("React.createElement(\"div\"", output);
    }

    [Fact]
    public async Task Capitalized_components_lower_to_an_identifier_callee()
    {
        var output = await Lower("function Comp() { return null; }\nexport const a = <Comp />;");
        // Not quoted — the component identifier is passed by reference.
        Assert.Contains("React.createElement(Comp", output);
        Assert.DoesNotContain("React.createElement(\"Comp\"", output);
    }

    [Fact]
    public async Task Member_expression_components_lower_to_a_member_callee()
    {
        var output = await Lower("const Lib = { Card: () => null };\nexport const a = <Lib.Card />;");
        Assert.Contains("React.createElement(Lib.Card", output);
    }

    [Fact]
    public async Task Attributes_become_a_props_object()
    {
        var output = await Lower("export const a = <div title=\"hi\" />;");
        Assert.Contains("React.createElement(\"div\", {", output);
        Assert.Contains("\"hi\"", output);
    }

    [Fact]
    public async Task No_props_pass_null_in_the_props_slot()
    {
        var output = await Lower("export const a = <div>{value}</div>;");
        // The expression child is a trailing argument, with null in the props slot.
        Assert.Contains("React.createElement(\"div\", null, value)", output);
    }

    [Fact]
    public async Task Fragments_lower_to_the_fragment_factory()
    {
        var output = await Lower("export const a = <>{first}{second}</>;");
        Assert.Contains("React.Fragment", output);
    }
}
