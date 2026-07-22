namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Printer;
using Xunit;

/// <summary>
/// netpack strips TypeScript natively (no external tsc). These guard that type
/// syntax is fully erased and the emitted output is plain, valid JavaScript with
/// the runtime values intact.
/// </summary>
public class TypeScriptStrippingTests
{
    private static string Strip(string source)
        => JsPrinter.Print(Parser.ParseModule(source, "in.ts"));

    private static void AssertPlainJs(string js)
    {
        // Re-parse with TypeScript *disabled*: any leftover type syntax would now
        // surface as a diagnostic (or be misparsed), so a clean parse proves the
        // erase was complete.
        var reparsed = Parser.ParseModule(js, "out.js", new ParserOptions { TypeScript = false, Jsx = false });
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public void Erases_parameter_and_return_annotations()
    {
        var js = Strip("function greet(name: string, times: number = 1): string { return name; }");
        Assert.Contains("function greet(name, times", js);
        Assert.DoesNotContain("string", js);
        Assert.DoesNotContain("number", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Erases_declaration_type_parameters()
    {
        var js = Strip("function identity<T>(x: T): T { return x; }\nconst r = identity(42);");
        Assert.DoesNotContain("<T>", js);
        Assert.DoesNotContain(": T", js);
        Assert.Contains("identity(42)", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Removes_interface_declarations()
    {
        var js = Strip("interface User { id: number; name: string; }\nexport const u = { id: 1, name: \"a\" };");
        Assert.DoesNotContain("interface", js);
        Assert.Contains("export const u", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Removes_type_aliases()
    {
        var js = Strip("type ID = string | number;\nconst x = 5;");
        Assert.DoesNotContain("type ID", js);
        Assert.DoesNotContain("| number", js);
        Assert.Contains("const x = 5", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Strips_assertion_operators()
    {
        var js = Strip("const a = value as Foo; const b = cfg satisfies Bar; const c = [1] as const; const d = obj!.x;");
        Assert.DoesNotContain("satisfies", js);
        Assert.DoesNotContain("as const", js);
        Assert.DoesNotContain("as Foo", js);
        Assert.DoesNotContain("!.", js);
        Assert.Contains("const a = value", js);
        Assert.Contains("obj.x", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Drops_type_only_imports_but_keeps_value_imports()
    {
        var js = Strip("import type { T } from './types';\nimport { fn } from './impl';\nconst k = fn;");
        Assert.DoesNotContain("./types", js);
        Assert.Contains("./impl", js);
        Assert.Contains("fn", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Drops_inline_type_specifiers()
    {
        var js = Strip("import { type A, b, type C } from './m';\nconst k = b;");
        Assert.DoesNotContain("type A", js);
        Assert.DoesNotContain("type C", js);
        Assert.Contains("b", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Erases_declare_statements()
    {
        var js = Strip("declare const G: number;\ndeclare function f(): void;\nconst live = 7;");
        Assert.DoesNotContain("declare", js);
        Assert.Contains("const live = 7", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Strips_class_member_modifiers_and_parameter_properties()
    {
        var js = Strip("class Svc { private count: number = 0; readonly name = \"x\"; constructor(public dep: Dep) {} inc(): void { this.count++; } }");
        Assert.DoesNotContain("private", js);
        Assert.DoesNotContain("readonly", js);
        Assert.DoesNotContain("public", js);
        Assert.DoesNotContain(": void", js);
        Assert.Contains("constructor(dep)", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Lowers_enum_to_a_runtime_value()
    {
        var js = Strip("enum Color { Red, Green, Blue }");
        // Unlike interfaces/types, an enum has a runtime representation.
        Assert.Contains("Color", js);
        Assert.Contains("Red", js);
        AssertPlainJs(js);
    }

    [Fact]
    public void Erases_namespaces_entirely()
    {
        // netpack does not emit namespace IIFEs — a namespace is treated as
        // type-only and dropped, while surrounding statements are kept.
        var js = Strip("namespace Utils { export const version = 1; }\nconst after = 2;");
        Assert.DoesNotContain("namespace", js);
        Assert.Contains("const after = 2", js);
        AssertPlainJs(js);
    }
}
