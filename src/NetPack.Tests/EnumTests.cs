namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Ast;
using NetPack.Syntax.Printer;
using Xunit;

public class EnumTests
{
    private static string Lower(string source)
    {
        var module = Parser.ParseModule(source, "test.ts");
        return JsPrinter.Print(module);
    }

    [Fact]
    public void Numeric_enum_gets_reverse_mapping_and_autoincrement()
    {
        var output = Lower("enum Color { Red, Green = 5, Blue }");
        Assert.Contains("const Color = (", output);
        Assert.Contains("Color[Color[\"Red\"] = 0] = \"Red\"", output);
        Assert.Contains("Color[Color[\"Green\"] = 5] = \"Green\"", output);
        Assert.Contains("Color[Color[\"Blue\"] = 6] = \"Blue\"", output);
    }

    [Fact]
    public void String_enum_is_forward_only()
    {
        var output = Lower("enum E { A = \"a\", B = \"b\" }");
        Assert.Contains("E[\"A\"] = \"a\"", output);
        Assert.Contains("E[\"B\"] = \"b\"", output);
        // No reverse mapping for string members.
        Assert.DoesNotContain("E[E[\"A\"]", output);
    }

    [Fact]
    public void Exported_enum_produces_exported_const()
    {
        var module = Parser.ParseModule("export enum Direction { Up, Down }", "test.ts");
        var export = Assert.IsType<ExportNamedDeclaration>(module.Body[0]);
        Assert.IsType<VariableStatement>(export.Declaration);
        var output = JsPrinter.Print(module);
        Assert.Contains("export const Direction", output);
    }

    [Fact]
    public void Const_enum_still_lowers()
    {
        var module = Parser.ParseModule("const enum Flags { A = 1, B = 2 }", "test.ts");
        Assert.IsType<VariableStatement>(module.Body[0]);
    }

    [Fact]
    public void Lowered_enum_reparses_cleanly()
    {
        var output = Lower("enum Color { Red, Green = 5, Blue }");
        var reparsed = Parser.ParseModule(output, "test.js");
        Assert.Empty(reparsed.Diagnostics);
    }
}
