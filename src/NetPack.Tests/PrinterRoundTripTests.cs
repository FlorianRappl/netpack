namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Printer;
using Xunit;

/// <summary>
/// Guards that the printer emits syntactically valid JavaScript: each snippet is
/// parsed, printed, and re-parsed; the re-parse must be diagnostic-free. This is
/// the property that keeps generated bundles free of "syntax error" surprises.
/// </summary>
public class PrinterRoundTripTests
{
    [Theory]
    // Destructuring defaults — the shorthand `{ a = 1 }` form must be preserved
    // (printing it as `{ a: 1 }` or `{ a }` is invalid pattern syntax).
    [InlineData("const { a = 1, b } = obj;")]
    [InlineData("const { a = 1, b = 2 } = obj;")]
    [InlineData("const { a = someDefault, b } = obj;")]
    [InlineData("function f({ a = 1, b } = {}) { return a + b; }")]
    [InlineData("const [x = 1, , z = 3] = arr;")]
    [InlineData("function g([a = 1, b] = []) { return a; }")]
    // Spread across positions.
    [InlineData("const a = [1, ...rest, 2]; f(...args); const o = { ...base, x: 1 };")]
    // Optional chaining and nullish coalescing.
    [InlineData("const v = a?.b?.().c ?? d;")]
    // Sequence expression.
    [InlineData("for (let i = 0, j = 10; i < j; i++, j--) {}")]
    // Object literal shorthand vs. explicit.
    [InlineData("const o = { a, b, c: 1, [k]: 2, m() {}, get x() { return 1; } };")]
    // Template literals.
    [InlineData("const s = `a${x}b${y + 1}c`;")]
    // Classes.
    [InlineData("class A extends B { s = 2; get v() { return this.s; } m(a, ...r) { return r; } }")]
    // Async / generators / arrows.
    [InlineData("const h = async (x) => { await x; }; async function* gen() { yield 1; }")]
    // Array holes stay valid.
    [InlineData("const arr = [1, , 3];")]
    // Expression statements whose leftmost token is `function`/`class`/`{` must be
    // parenthesized (IIFEs are pervasive in real/UMD bundles).
    [InlineData("(function () { console.log(1); })();")]
    [InlineData("(function named() { return 1; })();")]
    [InlineData("(function () {}).call(this);")]
    [InlineData("(function () {})().foo;")]
    [InlineData("(class {}).name;")]
    [InlineData("({}).toString();")]
    [InlineData("!function () {}();")]
    public void Prints_valid_javascript(string source)
    {
        var module = Parser.ParseModule(source, "in.js");
        Assert.Empty(module.Diagnostics); // sanity: the parser accepts the input

        var printed = JsPrinter.Print(module);
        var reparsed = Parser.ParseModule(printed, "out.js");

        Assert.Empty(reparsed.Diagnostics);
    }

    [Theory]
    // Compact output must not merge word keywords with a following statement
    // (`else`/`do` + `throw`/`switch`/identifier → `elsethrow` / `dox`).
    [InlineData("if (a) { b(); } else throw c;")]
    [InlineData("if (a) b(); else switch (c) { case 1: d(); break; }")]
    [InlineData("if (a) b(); else return d;")]
    [InlineData("do x(); while (y);")]
    [InlineData("if (a) b(); else if (c) d(); else e();")]
    // Parenthesized ternary consequents must not be mistaken for arrow params.
    [InlineData("var x = a ? (b) : c;")]
    [InlineData("var y = cond ? (f.name, g, h) : other;")]
    [InlineData("f.registrationName ? (ua) : (g, h);")]
    public void Prints_valid_compact_javascript(string source)
    {
        var module = Parser.ParseModule(source, "in.js");
        Assert.Empty(module.Diagnostics);

        var printed = JsPrinter.Print(module, PrinterOptions.Compact);
        Assert.Empty(Parser.ParseModule(printed, "out.js").Diagnostics);
    }

    [Fact]
    public void Parenthesized_ternary_consequent_is_not_an_arrow()
    {
        // `a ? (b) : c` is a conditional, not `(b) => ...`.
        var printed = JsPrinter.Print(Parser.ParseModule("var x = a ? (b) : c;", "in.js"));
        Assert.Contains("?", printed);
        Assert.DoesNotContain("=>", printed);
    }

    [Fact]
    public void Typescript_arrow_return_type_still_detected()
    {
        // The `:` return-type heuristic must still recognise real arrows in TS.
        var module = Parser.ParseModule("const f = (x): number => x + 1;", "in.ts", ParserOptions.Default);
        Assert.Empty(module.Diagnostics);
        Assert.Contains("=>", JsPrinter.Print(module));
    }

    [Fact]
    public void Iife_statement_is_parenthesized()
    {
        var printed = JsPrinter.Print(Parser.ParseModule("(function () { return 1; })();", "in.js")).Trim();

        // Must not begin with a bare `function` (which parses as a nameless
        // function *statement* and throws "Function statements require a name").
        Assert.StartsWith("(", printed);
        Assert.Empty(Parser.ParseModule(printed, "out.js").Diagnostics);
    }

    [Fact]
    public void Preserves_object_destructuring_default()
    {
        var printed = JsPrinter.Print(Parser.ParseModule("const { a = 1, b } = obj;", "in.js"));
        // The default must survive as `a = 1`, not `a: 1` and not a bare `a`.
        Assert.Contains("a = 1", printed);
        Assert.DoesNotContain("a: 1", printed);
    }

    [Fact]
    public void Preserves_identifier_default_in_destructuring()
    {
        // Regression: a default that is itself an identifier must not be mistaken
        // for plain shorthand and dropped.
        var printed = JsPrinter.Print(Parser.ParseModule("const { a = fallback } = obj;", "in.js"));
        Assert.Contains("a = fallback", printed);
    }

    [Fact]
    public void Plain_shorthand_stays_shorthand()
    {
        var printed = JsPrinter.Print(Parser.ParseModule("const o = { a, b };", "in.js"));
        Assert.DoesNotContain("a: a", printed);
        Assert.DoesNotContain("a = a", printed);
    }
}
