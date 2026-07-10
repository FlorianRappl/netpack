namespace NetPack.Tests;

using System.Linq;
using NetPack.Graph;
using NetPack.Syntax;
using NetPack.Syntax.Ast;
using NetPack.Syntax.Printer;
using Xunit;

public class ReactRefreshTests
{
    private static string Instrument(string source, int moduleId = 7)
    {
        var module = Parser.ParseModule(source, "mod.js");
        var body = ReactRefresh.Instrument([.. module.Body], moduleId);
        return JsPrinter.Print(new SourceFile("mod.js", body, System.Array.Empty<Diagnostic>()));
    }

    [Fact]
    public void Registers_function_components()
    {
        var output = Instrument("function App() { return null; }");

        Assert.Contains("$RefreshReg$", output);
        Assert.Contains("$RefreshReg$(App", output);
        Assert.Contains("\"7 \"", output);           // family id is prefixed with the module id
        Assert.Contains("__netpackRefresh.register", output);
    }

    [Fact]
    public void Installs_a_refresh_boundary()
    {
        var output = Instrument("function App() { return null; }");

        Assert.Contains("module.hot.accept", output);
        Assert.Contains("isBoundary(exports)", output);
        Assert.Contains("__netpackRefresh.perform", output);
    }

    [Fact]
    public void Registers_arrow_and_hoc_components()
    {
        var arrow = Instrument("const Button = () => null;");
        Assert.Contains("$RefreshReg$(Button", arrow);

        var hoc = Instrument("const Card = memo(function () { return null; });");
        Assert.Contains("$RefreshReg$(Card", hoc);

        var forwardRef = Instrument("const Input = React.forwardRef(function () { return null; });");
        Assert.Contains("$RefreshReg$(Input", forwardRef);
    }

    [Fact]
    public void Leaves_non_component_modules_untouched()
    {
        var output = Instrument("function helper() { return 1; }\nconst value = 42;");

        Assert.DoesNotContain("$RefreshReg$", output);
        Assert.DoesNotContain("module.hot.accept", output);
    }

    [Fact]
    public void Lowercase_and_plain_values_are_not_components()
    {
        // Uppercase but not a function/HOC → not a component.
        var output = Instrument("const API_URL = \"https://example.com\";\nconst Config = { a: 1 };");
        Assert.DoesNotContain("$RefreshReg$", output);
    }

    [Fact]
    public void Setup_wires_the_runtime()
    {
        var body = ReactRefresh.BuildSetup(3);
        var output = JsPrinter.Print(new SourceFile("setup.js", body, System.Array.Empty<Diagnostic>()));

        Assert.Contains("__r(3)", output);                 // requires the runtime module
        Assert.Contains("injectIntoGlobalHook", output);
        Assert.Contains("globalThis.__netpackRefresh", output);
        Assert.Contains("performReactRefresh", output);
        Assert.Contains("isLikelyComponentType", output);
    }

    [Fact]
    public void Instrumented_output_reparses_cleanly()
    {
        var output = Instrument("export function App() { return null; }\nfunction App2() { return null; }");
        Assert.Empty(Parser.ParseModule(output, "out.js").Diagnostics);
    }
}
