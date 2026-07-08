namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Printer;
using Xunit;

public class PrinterTests
{
    private static string Print(string source, bool minify = false)
    {
        var module = Parser.ParseModule(source, "test.js");
        return JsPrinter.Print(module, minify ? PrinterOptions.Compact : PrinterOptions.Pretty);
    }

    [Fact]
    public void Prints_binary_precedence_without_extra_parens()
    {
        Assert.Contains("1 + 2 * 3", Print("const x = 1 + 2 * 3;"));
    }

    [Fact]
    public void Inserts_parentheses_where_precedence_requires()
    {
        Assert.Contains("(1 + 2) * 3", Print("const x = (1 + 2) * 3;"));
    }

    [Fact]
    public void Preserves_right_associativity_of_exponent()
    {
        // 2 ** 3 ** 2 is right-associative; no parens needed.
        Assert.Contains("2 ** 3 ** 2", Print("const x = 2 ** 3 ** 2;"));
    }

    [Fact]
    public void Parenthesizes_left_exponent_operand()
    {
        Assert.Contains("(2 ** 3) ** 2", Print("const x = (2 ** 3) ** 2;"));
    }

    [Fact]
    public void Prints_arrow_function()
    {
        var output = Print("const f = (a, b) => a + b;");
        Assert.Contains("=>", output);
        Assert.Contains("a + b", output);
    }

    [Fact]
    public void Prints_object_shorthand_and_pairs()
    {
        var output = Print("const o = { a: 1, b };");
        Assert.Contains("a: 1", output);
        Assert.Contains("b", output);
    }

    [Fact]
    public void Prints_template_literal()
    {
        Assert.Contains("`a${x}b`", Print("const s = `a${x}b`;"));
    }

    [Fact]
    public void Minified_output_has_no_newlines()
    {
        var output = Print("const x = 1;\nconst y = 2;\nfunction f() { return x + y; }", minify: true);
        Assert.DoesNotContain("\n", output);
    }

    [Fact]
    public void Round_trips_complex_module_without_diagnostics()
    {
        const string source = """
            import { a, b as c } from './dep';
            export const add = (x, y) => x + y;
            export default class Widget extends Base {
                #count = 0;
                static kind = 'widget';
                constructor(name) { super(); this.name = name; }
                get count() { return this.#count; }
                increment() { this.#count++; }
            }
            const obj = { ...a, method() { return c?.value ?? 0; } };
            for (const item of items) { console.log(item); }
            """;
        var first = JsPrinter.Print(Parser.ParseModule(source, "test.js"));
        var reparsed = Parser.ParseModule(first, "test.js");
        Assert.Empty(reparsed.Diagnostics);
        // Printing again should be byte-for-byte stable.
        var second = JsPrinter.Print(reparsed);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Prints_class_with_members()
    {
        var output = Print("class A extends B { static x = 1; foo() { return 2; } }");
        Assert.Contains("class A extends B", output);
        Assert.Contains("static x = 1", output);
        Assert.Contains("foo()", output);
    }

    [Fact]
    public void Erases_types_in_output()
    {
        var module = Parser.ParseModule("const x: number = 1; interface I { a: string; }", "test.ts");
        var output = JsPrinter.Print(module);
        Assert.Contains("const x = 1", output);
        Assert.DoesNotContain("number", output);
        Assert.DoesNotContain("interface", output);
    }
}
