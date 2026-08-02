namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NetPack.Graph;

/// <summary>
/// The hook containers for one build — the compiler lifecycle and the per-
/// compilation phases (see <see cref="CompilerHooks"/> / <see cref="CompilationHooks"/>).
/// Preset hooks are registered here as taps (see <see cref="PresetHooks.Bind"/>),
/// and .NET plugins can tap the same points. A build with no taps pays nothing —
/// call sites gate on the relevant hook's <c>Count</c>.
/// </summary>
public sealed class BuildHooks
{
    /// <summary>Compiler-level lifecycle hooks.</summary>
    public CompilerHooks Compiler { get; } = new();

    /// <summary>Compilation-level phase hooks.</summary>
    public CompilationHooks Compilation { get; } = new();
}

/// <summary>
/// Runs a resolved hook module out-of-process (over the Node bridge in the CLI),
/// abstracted so the binding/tap logic is testable without spawning Node.
/// </summary>
public interface IHookRunner
{
    /// <summary>Invokes the hook module at <paramref name="modulePath"/> with
    /// <paramref name="payload"/>, returning its result (or null).</summary>
    Task<HookInvocation?> RunAsync(string modulePath, HookInvocation payload);
}

/// <summary>The JSON payload passed to (and returned from) a hook module.</summary>
public sealed class HookInvocation
{
    /// <summary>The hook name being invoked (e.g. <c>afterEmit</c>).</summary>
    [JsonPropertyName("hook")] public string? Hook { get; set; }

    /// <summary>The project root directory.</summary>
    [JsonPropertyName("root")] public string? Root { get; set; }

    /// <summary>True for a development build (dev server).</summary>
    [JsonPropertyName("dev")] public bool Dev { get; set; }

    /// <summary>The module file this hook fired for (module-level hooks only).</summary>
    [JsonPropertyName("module")] public string? Module { get; set; }

    /// <summary>Emitted assets (for asset-transforming hooks). On the return value,
    /// any entry with <see cref="HookAsset.Text"/> set replaces that asset.</summary>
    [JsonPropertyName("files")] public List<HookAsset>? Files { get; set; }

    /// <summary>For the <c>shouldEmit</c> hook: return <c>false</c> to skip writing.</summary>
    [JsonPropertyName("emit")] public bool? Emit { get; set; }
}

/// <summary>An emitted asset shared with (and optionally rewritten by) a hook.</summary>
public sealed class HookAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>The asset's text (for text outputs); null for binary assets.</summary>
    [JsonPropertyName("text")] public string? Text { get; set; }
}

/// <summary>
/// Binds resolved preset hook modules to <see cref="BuildHooks"/> as taps backed
/// by an <see cref="IHookRunner"/>. Every compiler/compilation hook is addressable
/// by its camelCase name; two friendly aliases (<c>beforeCompilation</c>,
/// <c>afterBundling</c>) are kept. The module list order (base-first, already
/// deduplicated by the resolver) is preserved via the tap stage.
/// </summary>
public static class PresetHooks
{
    // Friendly aliases kept for the canonical two lifecycle points.
    public const string BeforeCompilation = "beforeCompilation";
    public const string AfterBundling = "afterBundling";

    /// <summary>The mutable name→bytes asset map an asset hook reads and rewrites;
    /// the writer places it on the context before invoking.</summary>
    internal const string AssetsStateKey = "assets";

    public static void Bind(
        BuildHooks hooks,
        IReadOnlyDictionary<string, IReadOnlyList<string>> modules,
        IHookRunner runner,
        string root)
    {
        foreach (var (name, paths) in modules)
        {
            for (var stage = 0; stage < paths.Count; stage++)
            {
                Register(hooks, name, paths[stage], runner, root, stage);
            }
        }
    }

    private static void Register(BuildHooks hooks, string name, string module, IHookRunner runner, string root, int stage)
    {
        var c = hooks.Compiler;
        var p = hooks.Compilation;

        void Series<T>(SeriesHook<T> hook) where T : CompilerContext
            => hook.Tap(new NodeSeriesTap<T>(name, module, runner, root, stage));

        void Sync<T>(SyncHook<T> hook) where T : CompilerContext
            => hook.Tap(new NodeSyncTap<T>(name, module, runner, root, stage));

        switch (name)
        {
            // -- compiler lifecycle -----------------------------------------
            case "initialize": Sync(c.Initialize); break;
            case "beforeRun": Series(c.BeforeRun); break;
            case "run": Series(c.Run); break;
            case "watchRun": Series(c.WatchRun); break;
            case "beforeCompile" or BeforeCompilation: Series(c.BeforeCompile); break;
            case "compile": Sync(c.Compile); break;
            case "thisCompilation": Sync(c.ThisCompilation); break;
            case "compilation": Series(c.Compilation); break;
            case "make": Series(c.Make); break;
            case "finishMake": Series(c.FinishMake); break;
            case "afterCompile": Series(c.AfterCompile); break;
            case "shouldEmit": c.ShouldEmit.Tap(new NodeShouldEmitTap(module, runner, root, stage)); break;
            case "emit": Series(c.Emit); break;
            case "afterEmit" or AfterBundling: Series(c.AfterEmit); break;
            case "done": Series(c.Done); break;
            case "failed": Sync(c.Failed); break;
            case "invalid": Sync(c.Invalid); break;
            case "watchClose": Sync(c.WatchClose); break;
            case "shutdown": Series(c.Shutdown); break;

            // -- compilation: module lifecycle ------------------------------
            case "buildModule": Series(p.BuildModule); break;
            case "succeedModule": Series(p.SucceedModule); break;
            case "failedModule": Series(p.FailedModule); break;
            case "stillValidModule": Series(p.StillValidModule); break;
            case "finishModules": Series(p.FinishModules); break;

            // -- compilation: optimization ----------------------------------
            case "optimize": Series(p.Optimize); break;
            case "optimizeModules": Series(p.OptimizeModules); break;
            case "afterOptimizeModules": Series(p.AfterOptimizeModules); break;
            case "optimizeChunks": Series(p.OptimizeChunks); break;
            case "afterOptimizeChunks": Series(p.AfterOptimizeChunks); break;
            case "optimizeTree": Series(p.OptimizeTree); break;
            case "optimizeChunkModules": Series(p.OptimizeChunkModules); break;
            case "optimizeDependencies": Series(p.OptimizeDependencies); break;
            case "afterOptimizeDependencies": Series(p.AfterOptimizeDependencies); break;

            // -- compilation: ids, codegen, assets, sealing -----------------
            case "moduleIds": Series(p.ModuleIds); break;
            case "chunkIds": Series(p.ChunkIds); break;
            case "afterCodeGeneration": Series(p.AfterCodeGeneration); break;
            case "additionalAssets": Series(p.AdditionalAssets); break;
            case "processAssets": Series(p.ProcessAssets); break;
            case "afterProcessAssets": Series(p.AfterProcessAssets); break;
            case "seal": Series(p.Seal); break;
            case "contentHash": Series(p.ContentHash); break;
            case "afterSeal": Series(p.AfterSeal); break;

            default:
                Console.Error.WriteLine($"[netpack] Unknown preset hook '{name}' — ignored.");
                break;
        }
    }

    // Text outputs a hook receives as strings (and may return rewritten); other
    // assets (images, fonts) are passed by name only.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".css", ".html", ".htm", ".json", ".map", ".svg", ".txt", ".xml",
    };

    internal static bool IsText(string name)
        => TextExtensions.Contains(System.IO.Path.GetExtension(name));

    /// <summary>
    /// Builds the invocation payload from the hook context (including emitted
    /// assets and the module, when present), runs the module, and applies any
    /// returned asset rewrites back onto the shared asset map.
    /// </summary>
    private static async Task Invoke(string hookName, string module, IHookRunner runner, string root, CompilerContext context)
    {
        Dictionary<string, byte[]>? assets = null;
        List<HookAsset>? files = null;

        if (context.State.TryGetValue(AssetsStateKey, out var raw) && raw is Dictionary<string, byte[]> map)
        {
            assets = map;
            files = [];

            foreach (var (name, bytes) in assets)
            {
                if (IsText(name))
                {
                    files.Add(new HookAsset { Name = name, Text = Encoding.UTF8.GetString(bytes) });
                }
            }
        }

        var payload = new HookInvocation
        {
            Hook = hookName,
            Root = root,
            Dev = context.IsDevelopment,
            Module = (context as ModuleBuildContext)?.Module.FileName,
            Files = files,
        };

        var result = await runner.RunAsync(module, payload);

        if (assets is not null && result?.Files is { } outFiles)
        {
            foreach (var file in outFiles)
            {
                if (file.Name is not null && file.Text is not null)
                {
                    assets[file.Name] = Encoding.UTF8.GetBytes(file.Text);
                }
            }
        }
    }

    private sealed class NodeSeriesTap<TContext>(string hookName, string module, IHookRunner runner, string root, int stage)
        : IAsyncHookTap<TContext> where TContext : CompilerContext
    {
        public int Stage => stage;

        public Task RunAsync(TContext context) => Invoke(hookName, module, runner, root, context);
    }

    private sealed class NodeSyncTap<TContext>(string hookName, string module, IHookRunner runner, string root, int stage)
        : ISyncHookTap<TContext> where TContext : CompilerContext
    {
        public int Stage => stage;

        // Sync hooks are notifications; bridge execution is async, so block. Only
        // reached when a preset actually taps a sync hook.
        public void Run(TContext context) => Invoke(hookName, module, runner, root, context).GetAwaiter().GetResult();
    }

    private sealed class NodeShouldEmitTap(string module, IHookRunner runner, string root, int stage)
        : IAsyncBailHookTap<CompilationContext, bool>
    {
        public int Stage => stage;

        public async Task<bool> RunAsync(CompilationContext context)
        {
            var result = await runner.RunAsync(module, new HookInvocation
            {
                Hook = "shouldEmit",
                Root = root,
                Dev = context.IsDevelopment,
            });

            // The bail hook short-circuits on a non-default (true) result; a module
            // returning { emit: false } vetoes writing.
            return result?.Emit == false;
        }
    }
}
