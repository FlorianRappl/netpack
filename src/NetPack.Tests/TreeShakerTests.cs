namespace NetPack.Tests;

using NetPack.Syntax;
using NetPack.Syntax.Optimizer;
using NetPack.Syntax.Printer;
using Xunit;

public class TreeShakerTests
{
    private static string Shake(string source, params string[] usedExports)
    {
        var module = Parser.ParseModule(source, "mod.js");
        var used = new UsedExports();
        foreach (var name in usedExports) used.Add(name);
        TreeShaker.Shake(module, used);
        return JsPrinter.Print(module);
    }

    private static string ShakeAll(string source)
    {
        var module = Parser.ParseModule(source, "mod.js");
        TreeShaker.Shake(module, UsedExports.Everything());
        return JsPrinter.Print(module);
    }

    [Fact]
    public void Removes_unused_exported_function()
    {
        var output = Shake("export function used() {}\nexport function unused() {}", "used");
        Assert.Contains("used", output);
        Assert.DoesNotContain("unused", output);
    }

    [Fact]
    public void Keeps_transitively_referenced_helpers()
    {
        var output = Shake(
            "function helper() { return 1; }\nexport function used() { return helper(); }\nexport function dead() {}",
            "used");
        Assert.Contains("helper", output);
        Assert.Contains("used", output);
        Assert.DoesNotContain("dead", output);
    }

    [Fact]
    public void Removes_unreferenced_private_declaration()
    {
        var output = Shake("function privateHelper() {}\nexport function used() {}", "used");
        Assert.DoesNotContain("privateHelper", output);
        Assert.Contains("used", output);
    }

    [Fact]
    public void Preserves_top_level_side_effects()
    {
        var output = Shake("export function unusedThing() {}\nsideEffect();", "somethingElse");
        Assert.Contains("sideEffect()", output);
        Assert.DoesNotContain("unusedThing", output);
    }

    [Fact]
    public void Keeps_impure_declarations_even_when_export_unused()
    {
        // `register()` may have side effects, so the declaration must stay.
        var output = Shake("export const handle = register();", "other");
        Assert.Contains("register()", output);
    }

    [Fact]
    public void Prunes_unused_export_specifiers_and_their_declarations()
    {
        var output = Shake("const a = 1;\nconst b = 2;\nexport { a, b };", "a");
        Assert.Contains("a", output);
        Assert.DoesNotContain("b", output);
    }

    [Fact]
    public void Keeps_imports()
    {
        var output = Shake("import { dep } from './dep';\nexport function used() { return dep; }", "used");
        Assert.Contains("import", output);
        Assert.Contains("dep", output);
    }

    [Fact]
    public void Removes_unused_import_of_side_effect_free_module()
    {
        var module = Parser.ParseModule("import { a } from './x';\nexport function used() { return 1; }", "mod.js");
        var used = new UsedExports();
        used.Add("used");
        var removed = TreeShaker.Shake(module, used, _ => true); // ./x is side-effect-free
        var output = JsPrinter.Print(module);
        Assert.DoesNotContain("import", output);
        Assert.Single(removed);
    }

    [Fact]
    public void Keeps_unused_import_of_side_effectful_module()
    {
        var module = Parser.ParseModule("import './x';\nexport function used() {}", "mod.js");
        var used = new UsedExports();
        used.Add("used");
        var removed = TreeShaker.Shake(module, used, _ => false); // ./x has side effects
        var output = JsPrinter.Print(module);
        Assert.Contains("import", output);
        Assert.Empty(removed);
    }

    [Fact]
    public void Keeps_used_import_even_when_pure()
    {
        var module = Parser.ParseModule("import { a } from './x';\nexport function used() { return a; }", "mod.js");
        var used = new UsedExports();
        used.Add("used");
        var removed = TreeShaker.Shake(module, used, _ => true);
        Assert.Empty(removed);
        Assert.Contains("a", JsPrinter.Print(module));
    }

    [Fact]
    public void All_usage_still_drops_dead_private_declarations()
    {
        var output = ShakeAll("function deadPrivate() {}\nexport function api() {}");
        Assert.DoesNotContain("deadPrivate", output);
        Assert.Contains("api", output);
    }

    [Fact]
    public void Shaken_module_reparses_cleanly()
    {
        var output = Shake(
            "import { x } from './x';\nfunction h() { return x; }\nexport function keep() { return h(); }\nexport function drop() {}",
            "keep");
        Assert.Empty(Parser.ParseModule(output, "mod.js").Diagnostics);
        Assert.DoesNotContain("drop", output);
    }

    [Fact]
    public void Respects_shadowing_when_computing_liveness()
    {
        // The `helper` referenced inside `used` is a local parameter, not the
        // top-level `helper`, so the top-level one is dead.
        var output = Shake(
            "function helper() {}\nexport function used(helper) { return helper(); }",
            "used");
        Assert.Contains("function used(helper)", output);
        // The top-level helper() declaration is unreferenced (shadowed) → removed.
        Assert.DoesNotContain("function helper", output);
    }
}
