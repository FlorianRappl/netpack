namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Printer;
using Xunit;

/// <summary>
/// Broad "parse → print → re-parse" coverage: for each construct the parser must
/// accept the input, and the printed output must itself parse without diagnostics.
/// This is the cheapest guard against printer/parses regressions across the many
/// corners of modern JS/TS syntax a real bundle exercises.
/// </summary>
public class SyntaxRoundTripTests
{
    private static void RoundTrips(string source, string file)
    {
        var module = Parser.ParseModule(source, file);
        Assert.Empty(module.Diagnostics); // sanity: the parser accepts the input

        foreach (var options in new[] { PrinterOptions.Pretty, PrinterOptions.Compact })
        {
            var printed = JsPrinter.Print(module, options);
            var reparsed = Parser.ParseModule(printed, file);
            Assert.Empty(reparsed.Diagnostics);
        }
    }

    [Theory]
    // Assignment operators, exponentiation.
    [InlineData("a ||= b; a &&= c; a ??= d; a **= 2; a >>>= 1;")]
    [InlineData("const p = 2 ** 3 ** 2;")]
    // Numeric / BigInt literals with separators.
    [InlineData("const n = 1_000_000; const h = 0xffff; const big = 9_007n;")]
    // Optional catch binding, try/catch/finally.
    [InlineData("try { risky(); } catch { recover(); } finally { cleanup(); }")]
    [InlineData("try { a(); } catch (e) { b(e); }")]
    // Labels with continue/break.
    [InlineData("outer: for (;;) { for (;;) { continue outer; } break outer; }")]
    // Private fields / methods and a static initialization block.
    [InlineData("class C { #x = 1; #m() { return this.#x; } static { C.ready = true; } }")]
    // Computed + static + async + generator members.
    [InlineData("class D { static s = 1; [key]() {} async a() {} *g() {} }")]
    // Nested destructuring with defaults.
    [InlineData("const { a: { b = 1 } = {}, c: [d, , e = 2] = [] } = obj;")]
    [InlineData("function f(a = 1, { b } = {}, ...rest) { return [a, b, rest]; }")]
    // Generators / yield*, async arrows, for-await-of.
    [InlineData("function* g() { yield 1; yield* other(); }")]
    [InlineData("const f = async () => await g(); async function h() { for await (const x of s) { use(x); } }")]
    // Tagged templates and nested substitutions.
    [InlineData("tag`a${b}c${d + e}f`; const s = `${`nested ${x}`}`;")]
    // Regex vs. division disambiguation on one line.
    [InlineData("const re = /ab+c/gi; const q = a / b / c; if (/^x/.test(y)) z();")]
    // Meta properties.
    [InlineData("function f() { return new.target; } const u = import.meta.url;")]
    // Optional chaining variants.
    [InlineData("const v = a?.b?.[c]?.(d) ?? e; obj?.fn?.();")]
    // Arrow returning object literal must be parenthesized.
    [InlineData("const make = () => ({ a: 1, b: 2 }); const id = x => x;")]
    // Sequence in various positions.
    [InlineData("for (let i = 0, j = 9; i < j; i++, j--) {}")]
    // switch with default and fallthrough.
    [InlineData("switch (x) { case 1: case 2: y(); break; default: z(); }")]
    // do-while, while, labeled block.
    [InlineData("do work(); while (cond); block: { if (a) break block; }")]
    // Getters/setters in object literals.
    [InlineData("const o = { get x() { return 1; }, set x(v) { this._x = v; }, [k]: 2 };")]
    // Class expression and `new` chains.
    [InlineData("const C = class extends Base {}; const i = new C().method().value;")]
    // export forms.
    [InlineData("export const a = 1; export { a as default }; export * from './m'; export * as ns from './n';")]
    // Unicode and escapes in strings.
    [InlineData("const s = \"line\\nbreak\\t\\u0041\"; const t = 'it\\'s';")]
    public void Javascript_round_trips(string source) => RoundTrips(source, "in.js");

    [Theory]
    // Type annotations get erased but the value survives.
    [InlineData("let x: number = 1; const s: string = \"a\"; function f(a: number, b?: string): void {}")]
    // Generics on functions and classes.
    [InlineData("function id<T>(x: T): T { return x; }\nclass Box<T> { value: T; }\nconst r = id(1);")]
    // interface / type alias erased, value follows.
    [InlineData("interface I<T> { readonly a: T; b(): void; c?: string; }\nconst y = 1;")]
    [InlineData("type U = A | B & C; type Fn = (x: number) => string;\nconst z = 2;")]
    // enum lowering (numeric + string) still produces runnable JS.
    [InlineData("enum E { A, B = 5, C } enum S { X = \"x\", Y = \"y\" }")]
    // as / satisfies / as const / non-null.
    [InlineData("const a = v as number; const b = w satisfies Foo; const c = [1, 2] as const; const d = obj!.prop; f(x!);")]
    // Class with access modifiers, readonly, optional/definite, parameter properties.
    [InlineData("class C { private x: number = 1; readonly y = 2; z?: string; w!: number; constructor(public p: string) {} m<T>(a: T): void {} }")]
    // import type / export type / inline type specifiers.
    [InlineData("import type { A } from './a'; import { type B, c } from './b'; export type { D };\nconst k = c;")]
    // declare and namespace are erased entirely.
    [InlineData("declare const g: number; declare function h(): void;\nconst live = 1;")]
    [InlineData("namespace N { export const inner = 1; }\nconst after = 2;")]
    public void Typescript_round_trips(string source) => RoundTrips(source, "in.ts");

    [Fact]
    public void Arrow_returning_an_object_literal_keeps_its_parentheses()
    {
        // Regression: a concise arrow body that is an object literal must print as
        // `=> ({ ... })`, never `=> { ... }` (which parses as a block statement).
        var printed = JsPrinter.Print(Parser.ParseModule("const f = x => ({ a: 1, b: 2 });", "in.js"));
        Assert.Contains("({", printed);
        Assert.Empty(Parser.ParseModule(printed, "out.js").Diagnostics);
    }

    [Fact]
    public void Type_only_import_is_erased_from_output()
    {
        // Regression: `import type` carries no runtime module — it must not be
        // emitted (which would create a spurious runtime dependency).
        var printed = JsPrinter.Print(Parser.ParseModule(
            "import type { T } from './types';\nimport { fn } from './impl';\nconst k = fn;", "in.ts"));
        Assert.DoesNotContain("./types", printed);
        Assert.Contains("./impl", printed);
    }

    [Theory]
    // Element with attributes, spread, expression container, and mapped children.
    [InlineData("const a = <div id=\"x\" className={cls} {...props}>{items.map(i => <Item key={i} />)}</div>;")]
    // Fragment shorthand.
    [InlineData("const b = <>{x}<span>text</span></>;")]
    // Member-expression component and boolean/implicit attributes.
    [InlineData("const c = <Ns.Comp disabled data-x=\"1\">{y}</Ns.Comp>;")]
    // Nested/conditional children.
    [InlineData("const d = <ul>{list.length ? list.map(x => <li>{x}</li>) : <li>empty</li>}</ul>;")]
    public void Jsx_round_trips(string source) => RoundTrips(source, "in.tsx");
}
