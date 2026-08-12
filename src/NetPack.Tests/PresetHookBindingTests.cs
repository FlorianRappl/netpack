namespace NetPack.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetPack.Plugins;
using Xunit;

/// <summary>
/// Tests that resolved preset hooks bind to the build's hook containers and run
/// through an <see cref="IHookRunner"/> — exercised with a fake runner, so no Node
/// bridge is needed.
/// </summary>
public class PresetHookBindingTests
{
    private sealed class FakeRunner : IHookRunner
    {
        public List<string> Calls { get; } = [];
        public System.Func<string, HookInvocation, HookInvocation?>? OnRun { get; set; }

        public Task<HookInvocation?> RunAsync(string modulePath, HookInvocation payload)
        {
            Calls.Add(modulePath);
            return Task.FromResult(OnRun?.Invoke(modulePath, payload));
        }
    }

    /// <summary>Builds a hook binding list from bare module paths (test/options null).</summary>
    private static IReadOnlyList<HookBinding> Mods(params string[] modules)
        => modules.Select(m => new HookBinding { Module = m }).ToArray();

    [Fact]
    public async Task Before_compilation_hooks_bind_and_run_in_order()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner();
        var modules = new Dictionary<string, IReadOnlyList<HookBinding>>
        {
            ["beforeCompilation"] = Mods("/a.js", "/b.js"),
        };

        PresetHooks.Bind(hooks, modules, runner, "/root");

        Assert.Equal(2, hooks.Compiler.BeforeCompile.Count);

        await hooks.Compiler.BeforeCompile.CallAsync(new CompilerContext { IsDevelopment = false });

        Assert.Equal(new[] { "/a.js", "/b.js" }, runner.Calls); // preserved order
    }

    [Fact]
    public async Task After_bundling_hook_rewrites_asset_contents()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner
        {
            // Uppercase the text of app.js; leave others alone.
            OnRun = (_, payload) =>
            {
                var files = new List<HookAsset>();
                foreach (var f in payload.Files ?? [])
                {
                    if (f.Name == "app.js")
                    {
                        files.Add(new HookAsset { Name = f.Name, Text = f.Text!.ToUpperInvariant() });
                    }
                }
                return new HookInvocation { Files = files };
            },
        };

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>> { ["afterBundling"] = Mods("/transform.mjs") },
            runner,
            "/root");

        var assets = new Dictionary<string, byte[]>
        {
            ["app.js"] = Encoding.UTF8.GetBytes("console.log('hi');"),
            ["logo.png"] = new byte[] { 1, 2, 3 }, // binary — not passed as text
        };

        var context = new CompilationContext { BundlerContext = null!, OutputOptions = null };
        context.State["assets"] = assets;

        await hooks.Compiler.AfterEmit.CallAsync(context);

        Assert.Equal("CONSOLE.LOG('HI');", Encoding.UTF8.GetString(assets["app.js"]));
        Assert.Equal(new byte[] { 1, 2, 3 }, assets["logo.png"]); // untouched
    }

    [Fact]
    public void Known_hook_names_bind_to_their_containers()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner();

        PresetHooks.Bind(hooks, new Dictionary<string, IReadOnlyList<HookBinding>>
        {
            ["make"] = Mods("/m.js"),
            ["buildModule"] = Mods("/b.js"),
            ["optimizeModules"] = Mods("/o.js"),
            ["processAssets"] = Mods("/p.js"),
            ["contentHash"] = Mods("/h.js"),
            ["done"] = Mods("/d.js"),
            ["shouldEmit"] = Mods("/s.js"),
            ["afterBundling"] = Mods("/a.js"), // alias → afterEmit
        }, runner, "/root");

        Assert.Equal(1, hooks.Compiler.Make.Count);
        Assert.Equal(1, hooks.Compilation.BuildModule.Count);
        Assert.Equal(1, hooks.Compilation.OptimizeModules.Count);
        Assert.Equal(1, hooks.Compilation.ProcessAssets.Count);
        Assert.Equal(1, hooks.Compilation.ContentHash.Count);
        Assert.Equal(1, hooks.Compiler.Done.Count);
        Assert.Equal(1, hooks.Compiler.ShouldEmit.Count);
        Assert.Equal(1, hooks.Compiler.AfterEmit.Count);
    }

    [Fact]
    public void Unknown_hook_names_are_ignored()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner();

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>> { ["nonsense"] = Mods("/x.js") },
            runner,
            "/root");

        Assert.Equal(0, hooks.Compiler.BeforeCompile.Count);
        Assert.Equal(0, hooks.Compiler.AfterEmit.Count);
    }

    [Fact]
    public async Task Test_filter_scopes_an_asset_hook_to_matching_files()
    {
        var hooks = new BuildHooks();
        var seen = new List<string>();
        var runner = new FakeRunner
        {
            OnRun = (_, payload) =>
            {
                foreach (var f in payload.Files ?? []) seen.Add(f.Name!);
                return null;
            },
        };

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>>
            {
                ["afterBundling"] = new[] { new HookBinding { Module = "/t.mjs", Test = "\\.js$" } },
            },
            runner,
            "/root");

        var assets = new Dictionary<string, byte[]>
        {
            ["app.js"] = Encoding.UTF8.GetBytes("a"),
            ["app.css"] = Encoding.UTF8.GetBytes("b"),
        };
        var context = new CompilationContext { BundlerContext = null!, OutputOptions = null };
        context.State["assets"] = assets;

        await hooks.Compiler.AfterEmit.CallAsync(context);

        Assert.Equal(new[] { "app.js" }, seen); // only the .js file reached the hook
    }

    [Fact]
    public async Task Options_are_passed_through_to_the_hook()
    {
        var hooks = new BuildHooks();
        System.Text.Json.JsonElement? captured = null;
        var runner = new FakeRunner { OnRun = (_, payload) => { captured = payload.Options; return null; } };

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>>
            {
                ["done"] = new[] { new HookBinding { Module = "/d.js", Options = "{\"level\":3}" } },
            },
            runner,
            "/root");

        await hooks.Compiler.Done.CallAsync(new CompilationContext { BundlerContext = null!, OutputOptions = null, IsDevelopment = false });

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Value.GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task Exclude_filters_out_matching_files()
    {
        var hooks = new BuildHooks();
        var seen = new List<string>();
        var runner = new FakeRunner
        {
            OnRun = (_, payload) => { foreach (var f in payload.Files ?? []) seen.Add(f.Name!); return null; },
        };

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>>
            {
                ["afterBundling"] = new[] { new HookBinding { Module = "/t.mjs", Exclude = "\\.min\\.js$" } },
            },
            runner,
            "/root");

        var assets = new Dictionary<string, byte[]>
        {
            ["app.js"] = Encoding.UTF8.GetBytes("a"),
            ["app.min.js"] = Encoding.UTF8.GetBytes("b"),
        };
        var context = new CompilationContext { BundlerContext = null!, OutputOptions = null };
        context.State["assets"] = assets;

        await hooks.Compiler.AfterEmit.CallAsync(context);

        Assert.Equal(new[] { "app.js" }, seen); // the .min.js file was excluded
    }

    [Fact]
    public async Task Mode_scopes_a_hook_to_dev_or_prod_builds()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner();

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>>
            {
                ["done"] = new[] { new HookBinding { Module = "/d.js", Mode = "dev" } },
            },
            runner,
            "/root");

        await hooks.Compiler.Done.CallAsync(new CompilationContext { BundlerContext = null!, OutputOptions = null, IsDevelopment = false });
        Assert.Empty(runner.Calls); // optimized build: skipped

        await hooks.Compiler.Done.CallAsync(new CompilationContext { BundlerContext = null!, OutputOptions = null, IsDevelopment = true });
        Assert.Equal(new[] { "/d.js" }, runner.Calls); // dev build: ran
    }

    [Fact]
    public async Task Order_moves_a_hook_earlier_within_a_phase()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner();

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>>
            {
                ["beforeCompilation"] = new[]
                {
                    new HookBinding { Module = "/a.js" },             // order 0
                    new HookBinding { Module = "/b.js", Order = -1 }, // negative → earlier
                },
            },
            runner,
            "/root");

        await hooks.Compiler.BeforeCompile.CallAsync(new CompilerContext { IsDevelopment = false });

        Assert.Equal(new[] { "/b.js", "/a.js" }, runner.Calls);
    }

    [Fact]
    public async Task Name_is_passed_through_to_the_hook()
    {
        var hooks = new BuildHooks();
        string? capturedName = null;
        var runner = new FakeRunner { OnRun = (_, payload) => { capturedName = payload.Name; return null; } };

        PresetHooks.Bind(
            hooks,
            new Dictionary<string, IReadOnlyList<HookBinding>>
            {
                ["done"] = new[] { new HookBinding { Module = "/d.js", Name = "stamp" } },
            },
            runner,
            "/root");

        await hooks.Compiler.Done.CallAsync(new CompilationContext { BundlerContext = null!, OutputOptions = null, IsDevelopment = false });

        Assert.Equal("stamp", capturedName);
    }
}
