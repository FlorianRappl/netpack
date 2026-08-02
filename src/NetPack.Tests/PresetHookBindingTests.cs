namespace NetPack.Tests;

using System.Collections.Generic;
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

    [Fact]
    public async Task Before_compilation_hooks_bind_and_run_in_order()
    {
        var hooks = new BuildHooks();
        var runner = new FakeRunner();
        var modules = new Dictionary<string, IReadOnlyList<string>>
        {
            ["beforeCompilation"] = new[] { "/a.js", "/b.js" },
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
            new Dictionary<string, IReadOnlyList<string>> { ["afterBundling"] = new[] { "/transform.mjs" } },
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

        PresetHooks.Bind(hooks, new Dictionary<string, IReadOnlyList<string>>
        {
            ["make"] = new[] { "/m.js" },
            ["buildModule"] = new[] { "/b.js" },
            ["optimizeModules"] = new[] { "/o.js" },
            ["processAssets"] = new[] { "/p.js" },
            ["contentHash"] = new[] { "/h.js" },
            ["done"] = new[] { "/d.js" },
            ["shouldEmit"] = new[] { "/s.js" },
            ["afterBundling"] = new[] { "/a.js" }, // alias → afterEmit
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
            new Dictionary<string, IReadOnlyList<string>> { ["nonsense"] = new[] { "/x.js" } },
            runner,
            "/root");

        Assert.Equal(0, hooks.Compiler.BeforeCompile.Count);
        Assert.Equal(0, hooks.Compiler.AfterEmit.Count);
    }
}
