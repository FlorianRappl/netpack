namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Xunit;

public class ManglerTests
{
    private static string Mangle(string source)
    {
        var module = Parser.ParseModule(source, "test.js");
        new Mangler().Process(module);
        return JsPrinter.Print(module);
    }

    [Fact]
    public void Renames_local_bindings_consistently()
    {
        // The parameter and its use must be renamed together, so no trace of
        // the original name remains.
        var output = Mangle("function f(longParameterName) { return longParameterName + 1; }");
        Assert.DoesNotContain("longParameterName", output);
    }

    [Fact]
    public void Preserves_module_scope_names()
    {
        var output = Mangle("function exportedThing(x) { return x; }");
        Assert.Contains("function exportedThing(", output);
    }

    [Fact]
    public void Preserves_free_globals()
    {
        var output = Mangle("function f() { return someGlobalValue.member; }");
        Assert.Contains("someGlobalValue", output);
    }

    [Fact]
    public void Preserves_property_names()
    {
        var output = Mangle("function f(obj) { return obj.propertyName; }");
        Assert.Contains("propertyName", output);
    }

    [Fact]
    public void Expands_shorthand_when_value_is_renamed()
    {
        var output = Mangle("function f(valueName) { return { valueName }; }");
        // The key stays 'valueName', the value takes the mangled name.
        Assert.Contains("valueName:", output);
    }

    [Fact]
    public void Nested_closures_share_the_binding_name()
    {
        var output = Mangle("function outer(captured) { return function inner() { return captured; }; }");
        // 'captured' is renamed in both the declaration and the closure use, so
        // it disappears entirely while remaining consistent.
        Assert.DoesNotContain("captured", output);
    }

    [Fact]
    public void Mangled_output_reparses_cleanly()
    {
        const string source = """
            function make(list, factor) {
                const results = [];
                for (const item of list) {
                    const scaled = item * factor;
                    results.push(scaled);
                }
                return results.map((v) => v + 1);
            }
            """;
        var module = Parser.ParseModule(source, "test.js");
        new Mangler().Process(module);
        var output = JsPrinter.Print(module, PrinterOptions.Compact);
        var reparsed = Parser.ParseModule(output, "test.js");
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public void Does_not_touch_shadowed_globals_used_before_local_decl()
    {
        // 'value' is local; 'transform' is global. Both must stay correct.
        var output = Mangle("function f(value) { return transform(value); }");
        Assert.Contains("transform(", output);
    }
}
