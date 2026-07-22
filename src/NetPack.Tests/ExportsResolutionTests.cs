namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Bundles;
using Xunit;

public class ExportsResolutionTests
{
    private static readonly IReadOnlyList<string> Web = PlatformTargets.For(Platform.Web).Conditions;
    private static readonly IReadOnlyList<string> Node = PlatformTargets.For(Platform.Node).Conditions;

    // A package.json's "exports" value is parsed and wrapped in a Dependency
    // rooted at /pkg/package.json. The JsonDocument is intentionally not disposed
    // so the backing JsonElement stays valid for the assertion (mirrors how the
    // resolver keeps parsed manifests alive in the dependency cache).
    private static Dependency Dep(string exportsJson)
    {
        var json = "{\"name\":\"pkg\",\"version\":\"1.0.0\",\"exports\":" + exportsJson + "}";
        var doc = JsonDocument.Parse(json);
        return new Dependency("/pkg/package.json", doc.RootElement);
    }

    private static string? Norm(string? path) => path?.Replace('\\', '/');

    [Fact]
    public void String_shorthand_is_the_root_entry_only()
    {
        var dep = Dep("\"./index.mjs\"");

        Assert.EndsWith("/pkg/index.mjs", Norm(dep.ResolveExport(".", Web)));
        Assert.Null(dep.ResolveExport("./feature", Web));
    }

    [Fact]
    public void Conditions_object_prefers_esm_import_over_require()
    {
        var dep = Dep("{\"import\":\"./esm.mjs\",\"require\":\"./cjs.js\",\"default\":\"./index.js\"}");

        Assert.EndsWith("/pkg/esm.mjs", Norm(dep.ResolveExport(".", Web)));
        Assert.Null(dep.ResolveExport("./x", Web));
    }

    [Fact]
    public void Require_only_package_falls_through_to_default()
    {
        // netpack omits "require" from its conditions, so a dual package with no
        // "import" resolves via the always-matched "default".
        var dep = Dep("{\"require\":\"./cjs.js\",\"default\":\"./index.js\"}");

        Assert.EndsWith("/pkg/index.js", Norm(dep.ResolveExport(".", Web)));
    }

    [Fact]
    public void Browser_and_node_conditions_diverge_by_platform()
    {
        var dep = Dep("{\"browser\":\"./b.js\",\"node\":\"./n.js\",\"default\":\"./d.js\"}");

        Assert.EndsWith("/pkg/b.js", Norm(dep.ResolveExport(".", Web)));
        Assert.EndsWith("/pkg/n.js", Norm(dep.ResolveExport(".", Node)));
    }

    [Fact]
    public void Subpath_map_matches_exact_keys()
    {
        var dep = Dep("{\".\":\"./index.mjs\",\"./feature\":\"./feature.mjs\"}");

        Assert.EndsWith("/pkg/index.mjs", Norm(dep.ResolveExport(".", Web)));
        Assert.EndsWith("/pkg/feature.mjs", Norm(dep.ResolveExport("./feature", Web)));
        Assert.Null(dep.ResolveExport("./missing", Web));
    }

    [Fact]
    public void Subpath_map_entries_may_nest_conditions()
    {
        var dep = Dep("{\".\":{\"import\":\"./esm.mjs\",\"default\":\"./cjs.js\"}}");

        Assert.EndsWith("/pkg/esm.mjs", Norm(dep.ResolveExport(".", Web)));
    }

    [Fact]
    public void Wildcard_patterns_substitute_the_match()
    {
        var dep = Dep("{\"./*\":\"./dist/*.mjs\"}");

        Assert.EndsWith("/pkg/dist/foo.mjs", Norm(dep.ResolveExport("./foo", Web)));
        Assert.EndsWith("/pkg/dist/a/b.mjs", Norm(dep.ResolveExport("./a/b", Web)));
    }

    [Fact]
    public void Longest_pattern_prefix_wins()
    {
        var dep = Dep("{\"./*\":\"./dist/*.js\",\"./feature/*\":\"./feature/*.mjs\"}");

        Assert.EndsWith("/pkg/feature/x.mjs", Norm(dep.ResolveExport("./feature/x", Web)));
        Assert.EndsWith("/pkg/dist/other.js", Norm(dep.ResolveExport("./other", Web)));
    }

    [Fact]
    public void Array_targets_take_the_first_that_resolves()
    {
        // The first entry only matches on Node, so the web build skips it.
        var dep = Dep("{\".\":[{\"node\":\"./n.js\"},\"./b.mjs\"]}");

        Assert.EndsWith("/pkg/b.mjs", Norm(dep.ResolveExport(".", Web)));
        Assert.EndsWith("/pkg/n.js", Norm(dep.ResolveExport(".", Node)));
    }

    [Fact]
    public void Null_target_blocks_a_subpath()
    {
        var dep = Dep("{\"./private\":null,\"./ok\":\"./ok.mjs\"}");

        Assert.Null(dep.ResolveExport("./private", Web));
        Assert.EndsWith("/pkg/ok.mjs", Norm(dep.ResolveExport("./ok", Web)));
    }

    [Fact]
    public void Angular_style_manifest_resolves_the_fesm_bundle()
    {
        // @angular/* packages route their real entry through "default" while
        // listing tooling-only conditions (types, esm2022) first.
        var dep = Dep(
            "{\".\":{\"types\":\"./index.d.ts\",\"esm2022\":\"./esm2022/core.mjs\",\"default\":\"./fesm2022/core.mjs\"}}");

        Assert.EndsWith("/pkg/fesm2022/core.mjs", Norm(dep.ResolveExport(".", Web)));
    }

    [Fact]
    public void No_exports_field_reports_none()
    {
        var json = "{\"name\":\"pkg\",\"version\":\"1.0.0\",\"main\":\"./index.js\"}";
        var doc = JsonDocument.Parse(json);
        var dep = new Dependency("/pkg/package.json", doc.RootElement);

        Assert.False(dep.HasExports);
        Assert.Null(dep.ResolveExport(".", Web));
    }

    [Fact]
    public async Task Resolver_bundles_the_export_selected_entry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-exports-" + Path.GetRandomFileName());
        var pkg = Path.Combine(dir, "node_modules", "mypkg");
        Directory.CreateDirectory(pkg);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { hello } from 'mypkg';\nexport default hello;");

            await File.WriteAllTextAsync(Path.Combine(pkg, "package.json"),
                "{\"name\":\"mypkg\",\"version\":\"1.0.0\",\"exports\":{\".\":{\"import\":\"./esm.mjs\",\"require\":\"./cjs.js\"}}}");
            await File.WriteAllTextAsync(Path.Combine(pkg, "esm.mjs"), "export const hello = 'ESM_MARKER';");
            await File.WriteAllTextAsync(Path.Combine(pkg, "cjs.js"), "module.exports = { hello: 'CJS_MARKER' };");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("ESM_MARKER", output);
            Assert.DoesNotContain("CJS_MARKER", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Resolver_follows_a_subpath_export()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netpack-exports-" + Path.GetRandomFileName());
        var pkg = Path.Combine(dir, "node_modules", "mypkg");
        Directory.CreateDirectory(Path.Combine(pkg, "internal"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.js"),
                "import { sub } from 'mypkg/feature';\nexport default sub;");

            await File.WriteAllTextAsync(Path.Combine(pkg, "package.json"),
                "{\"name\":\"mypkg\",\"version\":\"1.0.0\",\"exports\":{\"./feature\":\"./internal/feature.mjs\"}}");
            await File.WriteAllTextAsync(Path.Combine(pkg, "internal", "feature.mjs"),
                "export const sub = 'SUBPATH_MARKER';");

            using var graph = await Traverse.From(
                Path.Combine(dir, "main.js"), Array.Empty<string>(), Array.Empty<string>());
            var bundle = graph.Context.Bundles.Values.OfType<JsBundle>().First(b => b.IsPrimary);
            var output = bundle.Stringify(new OutputOptions { IsOptimizing = false, IsReloading = false });

            Assert.Contains("SUBPATH_MARKER", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
