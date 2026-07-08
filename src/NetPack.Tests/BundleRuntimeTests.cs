namespace NetPack.Tests;

using System;
using NetPack.Graph.Bundles;
using NetPack.Syntax;
using NetPack.Syntax.Minifier;
using NetPack.Syntax.Printer;
using Xunit;

public class BundleRuntimeTests
{
    private static void AssertParses(string source)
    {
        var module = Parser.ParseModule(source, "runtime.js",
            new ParserOptions { Tolerant = true, Jsx = false, TypeScript = false });
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Entry_runtime_parses_cleanly()
    {
        AssertParses(JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: false));
    }

    [Fact]
    public void Reloading_runtime_parses_cleanly_and_exposes_hmr()
    {
        var source = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: true);
        AssertParses(source);
        Assert.Contains("mod.hot = __hot(id)", source);
        Assert.Contains("function __apply(updates)", source);
        Assert.Contains("globalThis.__netpack", source);
    }

    [Fact]
    public void Reloading_runtime_tracks_dependency_graph_and_boundaries()
    {
        var source = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: true);
        // Records importers (parents) so updates can bubble upward…
        Assert.Contains("rec.parents.push(id)", source);
        // …to the nearest accept boundary, otherwise a full reload.
        Assert.Contains("rec.accepted", source);
        Assert.Contains("location.reload()", source);
        // Disposers run for outdated modules before re-execution.
        Assert.Contains("rec.disposers", source);
    }

    [Fact]
    public void Production_runtime_has_no_hmr()
    {
        var source = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: false);
        Assert.DoesNotContain("__hot", source);
        Assert.DoesNotContain("__netpack", source);
    }

    [Fact]
    public void Require_registers_exports_before_running_factory()
    {
        // Cache-before-run is what makes circular dependencies observe the
        // in-progress exports rather than undefined.
        var source = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: false);
        var cacheAssign = source.IndexOf("__c[id] = { exports: {} }", StringComparison.Ordinal);
        var factoryCall = source.IndexOf("__m[id](mod", StringComparison.Ordinal);
        Assert.True(cacheAssign >= 0 && factoryCall >= 0);
        Assert.True(cacheAssign < factoryCall, "the cache entry must be created before the factory runs");
    }

    [Fact]
    public void Shared_runtime_merges_registries()
    {
        var source = JsRuntime.Build(isShared: true, new[] { "__s0", "__s1" }, reloading: false);
        Assert.Contains("Object.assign(__m, __s0, __s1)", source);
        // Shared bundles do not own the require runtime.
        Assert.DoesNotContain("function __r", source);
    }

    [Fact]
    public void Whole_bundle_shape_is_valid_javascript()
    {
        // Mirrors the shape JsBundle assembles: registry of factories, runtime,
        // then the entry require + re-exports.
        var registry =
            "const __m = {\n" +
            "  0: (module, exports, require) => { exports.value = 41; },\n" +
            "  1: (module, exports, require) => { const { value } = require(0); exports.result = value + 1; }\n" +
            "};\n";
        var runtime = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: false);
        var trailer = "const { result } = __r(1);\nexport { result };\n";
        var full = registry + runtime + trailer;

        var module = Parser.ParseModule(full, "bundle.js");
        Assert.Empty(module.Diagnostics);

        // And it survives a mangling + compact print + reparse round-trip.
        new Mangler().Process(module);
        var printed = JsPrinter.Print(module, PrinterOptions.Compact);
        var reparsed = Parser.ParseModule(printed, "bundle.min.js");
        Assert.Empty(reparsed.Diagnostics);
    }

    [Fact]
    public void Reloading_bundle_with_hot_accept_round_trips()
    {
        var registry =
            "const __m = {\n" +
            "  0: (module, exports, require) => { exports.x = 1; if (module.hot) module.hot.accept(); }\n" +
            "};\n";
        var runtime = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: true);
        var trailer = "const { x } = __r(0);\nexport { x };\n";

        var module = Parser.ParseModule(registry + runtime + trailer, "bundle.js");
        Assert.Empty(module.Diagnostics);

        new Mangler().Process(module);
        var output = JsPrinter.Print(module, PrinterOptions.Compact);
        Assert.Empty(Parser.ParseModule(output, "bundle.min.js").Diagnostics);

        // The module.hot API surface is property access, so it must survive mangling.
        Assert.Contains("hot", output);
        Assert.Contains("accept", output);
    }

    [Fact]
    public void Mangler_preserves_runtime_and_export_symbols()
    {
        var registry = "const __m = { 0: (module, exports, require) => { exports.value = 1; } };\n";
        var runtime = JsRuntime.Build(isShared: false, Array.Empty<string>(), reloading: false);
        var trailer = "const { value } = __r(0);\nexport { value };\n";
        var module = Parser.ParseModule(registry + runtime + trailer, "bundle.js");
        new Mangler().Process(module);
        var output = JsPrinter.Print(module, PrinterOptions.Compact);

        // Module-scope runtime + the export interface must survive mangling.
        Assert.Contains("__m", output);
        Assert.Contains("__r", output);
        Assert.Contains("export", output);
        Assert.Contains("value", output); // exported name preserved
    }
}
