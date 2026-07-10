namespace NetPack.Tests;

using System.Collections.Generic;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Xunit;

public class SourceMapTests
{
    // Wraps a parsed module's body in a factory-style block tagged with its
    // source, mirroring how JsBundle drives source-mapped printing.
    private static (string Code, string Json, SourceMapBuilder Builder) PrintWithMap(string source, bool minify = false)
    {
        var module = Parser.ParseModule(source, "/project/src/app.js");
        var block = new BlockStatement(module.Body) { Source = module };
        var arrow = new ArrowFunctionExpression(new List<Parameter>(), block, async: false);
        var builder = new SourceMapBuilder("app.js", "/project");
        var code = JsPrinter.Print(arrow, minify ? PrinterOptions.Compact : PrinterOptions.Pretty, builder);
        return (code, builder.ToJson(), builder);
    }

    [Fact]
    public void Produces_valid_v3_map()
    {
        var (_, json, builder) = PrintWithMap("const x = 1;\nconst y = x + 2;\nconsole.log(y);");
        Assert.Contains("\"version\":3", json);
        Assert.Contains("\"file\":\"app.js\"", json);
        Assert.Contains("\"mappings\":\"", json);
        Assert.False(builder.IsEmpty);
    }

    [Fact]
    public void Sources_are_relative_and_content_is_inlined()
    {
        var (_, json, _) = PrintWithMap("export const answer = 42;");
        // Relative to the provided root, forward-slashed.
        Assert.Contains("\"sources\":[\"src/app.js\"]", json);
        // Original text embedded so devtools need not fetch the file.
        Assert.Contains("export const answer = 42;", json);
    }

    [Fact]
    public void Mappings_are_emitted_per_generated_line()
    {
        var (_, json, _) = PrintWithMap("const a = 1;\nconst b = 2;\nconst c = 3;");
        // Several generated lines → several ';'-separated groups with segments.
        var mappingsStart = json.IndexOf("\"mappings\":\"", System.StringComparison.Ordinal);
        var mappings = json[(mappingsStart + "\"mappings\":\"".Length)..];
        Assert.Contains(";", mappings);
    }

    [Fact]
    public void Mapping_survives_minification_and_mangling()
    {
        // Names change, positions (Start offsets) do not, so mappings still point
        // at the original source.
        var module = Parser.ParseModule("function greet(name) { return name; }\nexport const g = greet;", "/project/src/x.js");
        new Mangler().Process(module);
        var block = new BlockStatement(module.Body) { Source = module };
        var arrow = new ArrowFunctionExpression(new List<Parameter>(), block, async: false);
        var builder = new SourceMapBuilder("x.js", "/project");
        JsPrinter.Print(arrow, PrinterOptions.Compact, builder);
        Assert.False(builder.IsEmpty);
        Assert.Contains("\"version\":3", builder.ToJson());
    }

    [Fact]
    public void No_map_state_without_a_source_block()
    {
        // Printing without a source-tagged block yields an empty map.
        var module = Parser.ParseModule("const x = 1;", "a.js");
        var builder = new SourceMapBuilder("a.js", "");
        JsPrinter.Print(module, PrinterOptions.Pretty, builder);
        Assert.True(builder.IsEmpty);
    }
}
