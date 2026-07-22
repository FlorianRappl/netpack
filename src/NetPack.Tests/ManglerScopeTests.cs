namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Xunit;

/// <summary>
/// Scope-correctness edge cases for the mangler: locals are renamed consistently,
/// while property names, free globals, and module-scope symbols are preserved and
/// the output stays valid after a compact print.
/// </summary>
public class ManglerScopeTests
{
    private static string Mangle(string source, PrinterOptions? options = null)
    {
        var module = Parser.ParseModule(source, "test.js");
        new Mangler().Process(module);
        return JsPrinter.Print(module, options ?? PrinterOptions.Pretty);
    }

    private static void AssertReparses(string js)
        => Assert.Empty(Parser.ParseModule(js, "out.js").Diagnostics);

    [Fact]
    public void Free_global_survives_while_local_is_renamed()
    {
        var output = Mangle("function f(localBinding) { return globalHelper(localBinding); }");
        Assert.Contains("globalHelper(", output);
        Assert.DoesNotContain("localBinding", output);
        AssertReparses(output);
    }

    [Fact]
    public void Catch_scope_stays_valid()
    {
        var output = Mangle("try { attempt(); } catch (caughtError) { handleFailure(caughtError); }", PrinterOptions.Compact);
        // The caught-error handler and the surrounding globals must stay wired up.
        Assert.Contains("handleFailure(", output);
        Assert.Contains("attempt(", output);
        AssertReparses(output);
    }

    [Fact]
    public void Default_parameter_referencing_an_earlier_parameter_stays_valid()
    {
        // `b`'s default reads `a`; both are renamed but must remain consistent.
        var output = Mangle("function build(first, second = first) { return first + second; }");
        Assert.Contains("function build(", output); // module-scope name preserved
        AssertReparses(output);
    }

    [Fact]
    public void Destructuring_parameter_keeps_property_keys()
    {
        var output = Mangle("function pick({ alpha, beta }) { return alpha + beta; }");
        // The object keys are property names and must be preserved even as the
        // bound locals are renamed.
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        AssertReparses(output);
    }

    [Fact]
    public void Nested_function_declaration_is_renamed_consistently()
    {
        var output = Mangle("function outerFn() { return innerFn(); function innerFn() { return 42; } }");
        Assert.Contains("function outerFn(", output); // module scope kept
        AssertReparses(output);
    }

    [Fact]
    public void Class_method_locals_are_renamed_but_members_preserved()
    {
        var output = Mangle("class Widget { render(longLocalName) { const scratch = longLocalName + 1; return scratch; } }");
        Assert.Contains("render(", output);   // method name (a property) preserved
        Assert.DoesNotContain("longLocalName", output);
        AssertReparses(output);
    }

    [Fact]
    public void This_and_super_and_member_names_are_never_renamed()
    {
        var output = Mangle("class C extends Base { method() { return super.method() + this.field; } }");
        Assert.Contains("super.method()", output);
        Assert.Contains("this.field", output);
        AssertReparses(output);
    }

    [Fact]
    public void Block_scoped_shadowing_stays_valid_after_mangling()
    {
        var output = Mangle(
            "function scope(value) { { let value = compute(value); use(value); } return value; }",
            PrinterOptions.Compact);
        // The inner `let value` shadows the parameter; globals stay intact.
        Assert.Contains("compute(", output);
        Assert.Contains("use(", output);
        AssertReparses(output);
    }
}
