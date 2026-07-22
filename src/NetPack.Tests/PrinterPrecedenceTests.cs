namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Printer;
using Xunit;

/// <summary>
/// Operator-precedence and parenthesization coverage. The core guard is
/// idempotency: printing a parsed expression, re-parsing, and printing again must
/// produce identical text. A dropped-or-added paren changes the re-parsed AST, so
/// the second print diverges — exactly the class of bug that turns valid code into
/// a subtly different (or invalid) program.
/// </summary>
public class PrinterPrecedenceTests
{
    private static void Idempotent(string source, string file = "in.js")
    {
        var first = Parser.ParseModule(source, file);
        Assert.Empty(first.Diagnostics); // the input is valid

        var once = JsPrinter.Print(first);
        var second = Parser.ParseModule(once, file);
        Assert.Empty(second.Diagnostics); // the printed output is valid…

        var twice = JsPrinter.Print(second);
        Assert.Equal(once, twice); // …and stable, so precedence survived the trip
    }

    [Theory]
    // Arithmetic grouping and associativity.
    [InlineData("const a = (1 + 2) * 3;")]
    [InlineData("const b = 1 + 2 * 3;")]
    [InlineData("const c = 2 ** 3 ** 2;")]
    [InlineData("const d = a - (b - c);")]
    [InlineData("const e = a * (b + c) - d / (g - h);")]
    [InlineData("const f = (-a) ** b;")]
    // Logical vs. nullish (mixing requires explicit parens — dropping them is a
    // *syntax error*, so the second parse would fail loudly).
    [InlineData("const g = a || b && c;")]
    [InlineData("const h = (a || b) && c;")]
    [InlineData("const i = (a || b) ?? c;")]
    [InlineData("const j = a ?? (b || c);")]
    // Ternary nesting.
    [InlineData("const k = a ? b : c ? d : e;")]
    [InlineData("const l = (a ? b : c) ? d : e;")]
    [InlineData("const m = a ? (b = c) : d;")]
    // Assignment right-associativity, unary and comparison mixing.
    [InlineData("n = a = b;")]
    [InlineData("const o = typeof x === \"string\";")]
    [InlineData("const p = !a && !b || c;")]
    [InlineData("const q = -x.y.z;")]
    // new / call / member chains.
    [InlineData("const r = new A.B().c;")]
    [InlineData("const s = new (getClass())();")]
    [InlineData("const t = a.b().c[d].e;")]
    // Sequence expressions in argument and initializer position.
    [InlineData("f((a, b), c);")]
    [InlineData("const u = (x, y, z);")]
    // Spread.
    [InlineData("const v = [...a, b, ...c]; const w = { ...base, k: 1 };")]
    // Optional chaining precedence.
    [InlineData("const y = a?.b.c?.[d]?.(e);")]
    // Immediately-invoked arrow and function.
    [InlineData("const z = (x => x + 1)(5); (() => {})();")]
    public void Precedence_survives_a_print_reparse_round_trip(string source) => Idempotent(source);

    [Fact]
    public void Preserves_required_parentheses_around_lower_precedence_operands()
    {
        var printed = JsPrinter.Print(Parser.ParseModule("const x = (a + b) * c;", "in.js"));
        Assert.Contains("(a + b)", printed);
        Assert.DoesNotContain("a + b * c", printed);
    }

    [Fact]
    public void Preserves_parentheses_for_right_operand_of_left_associative_op()
    {
        var printed = JsPrinter.Print(Parser.ParseModule("const x = a - (b - c);", "in.js"));
        Assert.Contains("(b - c)", printed);
    }

    [Fact]
    public void Does_not_add_parentheses_to_a_right_associative_ternary_chain()
    {
        // `a ? b : c ? d : e` needs no parens around the alternate ternary.
        var printed = JsPrinter.Print(Parser.ParseModule("const x = a ? b : c ? d : e;", "in.js"));
        Assert.DoesNotContain("(c ? d : e)", printed);
    }

    [Fact]
    public void Keeps_the_nullish_logical_mix_parenthesized_and_valid()
    {
        // Regression guard: `(a || b) ?? c` must not print as `a || b ?? c`,
        // which is a SyntaxError.
        var printed = JsPrinter.Print(Parser.ParseModule("const x = (a || b) ?? c;", "in.js"));
        Assert.Empty(Parser.ParseModule(printed, "out.js").Diagnostics);
        Assert.Contains("??", printed);
    }
}
