namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
/// A resolved hook: the module to run, plus the optional <c>test</c> filter and
/// <c>options</c> from its preset entry (see <c>HookEntry</c>). A plain string
/// entry becomes a binding with <see cref="Test"/> and <see cref="Options"/> null
/// (match everything, no options).
/// </summary>
public sealed class HookBinding
{
    /// <summary>The resolved absolute module path.</summary>
    public required string Module { get; init; }

    /// <summary>Regex source matched against the file/module name, or null for all.</summary>
    public string? Test { get; init; }

    /// <summary>Regex source for names to skip (applied after <see cref="Test"/>), or null.</summary>
    public string? Exclude { get; init; }

    /// <summary><c>dev</c>/<c>prod</c>/<c>both</c> — which builds the hook runs in, or null (both).</summary>
    public string? Mode { get; init; }

    /// <summary>Ordering nudge within the phase; lower runs earlier (default 0).</summary>
    public int Order { get; init; }

    /// <summary>A label for diagnostics, also passed to the hook, or null.</summary>
    public string? Name { get; init; }

    /// <summary>Caller options as raw JSON, or null (the hook sees <c>{}</c>).</summary>
    public string? Options { get; init; }
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

    /// <summary>Caller-supplied options from the hook entry (default <c>{}</c>),
    /// passed straight through to the hook function as <c>payload.options</c>.</summary>
    [JsonPropertyName("options")] public JsonElement? Options { get; set; }

    /// <summary>The hook entry's <c>name</c> label, when set.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
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

    // The array index keeps base-first order; `order` shifts a hook to an earlier
    // (negative) or later (positive) band. This scale keeps indices from colliding
    // across bands for any realistic hook count.
    private const int OrderScale = 1_000_000;

    public static void Bind(
        BuildHooks hooks,
        IReadOnlyDictionary<string, IReadOnlyList<HookBinding>> modules,
        IHookRunner runner,
        string root)
    {
        foreach (var (name, bindings) in modules)
        {
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                var stage = binding.Order * OrderScale + index;
                Register(hooks, name, binding, runner, root, stage);
            }
        }
    }

    private static void Register(BuildHooks hooks, string name, HookBinding binding, IHookRunner runner, string root, int stage)
    {
        var c = hooks.Compiler;
        var p = hooks.Compilation;
        var scope = new HookScope(
            CompileTest(binding.Test, "test", binding.Name),
            CompileTest(binding.Exclude, "exclude", binding.Name),
            binding.Mode,
            binding.Name,
            ParseOptions(binding.Options));

        void Series<T>(SeriesHook<T> hook) where T : CompilerContext
            => hook.Tap(new NodeSeriesTap<T>(name, binding.Module, scope, runner, root, stage));

        void Sync<T>(SyncHook<T> hook) where T : CompilerContext
            => hook.Tap(new NodeSyncTap<T>(name, binding.Module, scope, runner, root, stage));

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
            case "shouldEmit": c.ShouldEmit.Tap(new NodeShouldEmitTap(binding.Module, scope, runner, root, stage)); break;
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
                var label = binding.Name is { } n ? $" ({n})" : "";
                Console.Error.WriteLine($"[netpack] Unknown preset hook '{name}'{label} — ignored.");
                break;
        }
    }

    /// <summary>The compiled, per-entry scoping applied around a hook invocation.</summary>
    private readonly record struct HookScope(Regex? Test, Regex? Exclude, string? Mode, string? Name, JsonElement? Options);

    /// <summary>True when a hook's <c>mode</c> allows it to run in this build.</summary>
    private static bool ModeApplies(string? mode, bool dev)
        => mode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "both" or "all" => true,
            "dev" or "development" or "serve" => dev,
            "prod" or "production" or "build" => !dev,
            _ => true,
        };

    // Text outputs a hook receives as strings (and may return rewritten); other
    // assets (images, fonts) are passed by name only.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".css", ".html", ".htm", ".json", ".map", ".svg", ".txt", ".xml",
    };

    internal static bool IsText(string name)
        => TextExtensions.Contains(System.IO.Path.GetExtension(name));

    /// <summary>Compiles a hook entry's <c>test</c>/<c>exclude</c> regex once; a
    /// malformed pattern is reported and treated as "no filter" rather than
    /// failing the build.</summary>
    private static Regex? CompileTest(string? pattern, string which, string? name)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern);
        }
        catch (ArgumentException)
        {
            var label = name is { } n ? $" ({n})" : "";
            Console.Error.WriteLine($"[netpack] Invalid hook {which} regex '{pattern}'{label} — ignoring it.");
            return null;
        }
    }

    /// <summary>Parses a hook entry's raw <c>options</c> JSON once, up front.</summary>
    private static JsonElement? ParseOptions(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the invocation payload from the hook context (including emitted
    /// assets and the module, when present), runs the module, and applies any
    /// returned asset rewrites back onto the shared asset map. The
    /// <paramref name="scope"/> constrains the hook: <c>mode</c> gates the build
    /// kind, and <c>test</c>/<c>exclude</c> scope the module or the asset files it
    /// receives (and can rewrite).
    /// </summary>
    private static async Task Invoke(string hookName, string module, HookScope scope, IHookRunner runner, string root, CompilerContext context)
    {
        // `mode`: skip a hook that doesn't apply to this build (dev vs optimized).
        if (!ModeApplies(scope.Mode, context.IsDevelopment))
        {
            return;
        }

        var modulePath = (context as ModuleBuildContext)?.Module.FileName;

        // Module-level hook: `test`/`exclude` scope which modules it runs for.
        if (modulePath is not null && !Matches(scope, modulePath))
        {
            return;
        }

        Dictionary<string, byte[]>? assets = null;
        List<HookAsset>? files = null;

        if (context.State.TryGetValue(AssetsStateKey, out var raw) && raw is Dictionary<string, byte[]> map)
        {
            assets = map;
            files = [];

            foreach (var (name, bytes) in assets)
            {
                if (IsText(name) && Matches(scope, name))
                {
                    files.Add(new HookAsset { Name = name, Text = Encoding.UTF8.GetString(bytes) });
                }
            }

            // A filtered asset hook with nothing to act on: don't spin up the bridge.
            if ((scope.Test is not null || scope.Exclude is not null) && files.Count == 0)
            {
                return;
            }
        }

        var payload = new HookInvocation
        {
            Hook = hookName,
            Root = root,
            Dev = context.IsDevelopment,
            Module = modulePath,
            Files = files,
            Options = scope.Options,
            Name = scope.Name,
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

    /// <summary>Applies a scope's <c>test</c> (must match) and <c>exclude</c> (must
    /// not match) to a file/module name.</summary>
    private static bool Matches(HookScope scope, string name)
    {
        if (scope.Test is not null && !scope.Test.IsMatch(name))
        {
            return false;
        }

        return scope.Exclude is null || !scope.Exclude.IsMatch(name);
    }

    private sealed class NodeSeriesTap<TContext>(string hookName, string module, HookScope scope, IHookRunner runner, string root, int stage)
        : IAsyncHookTap<TContext> where TContext : CompilerContext
    {
        public int Stage => stage;

        public Task RunAsync(TContext context) => Invoke(hookName, module, scope, runner, root, context);
    }

    private sealed class NodeSyncTap<TContext>(string hookName, string module, HookScope scope, IHookRunner runner, string root, int stage)
        : ISyncHookTap<TContext> where TContext : CompilerContext
    {
        public int Stage => stage;

        // Sync hooks are notifications; bridge execution is async, so block. Only
        // reached when a preset actually taps a sync hook.
        public void Run(TContext context) => Invoke(hookName, module, scope, runner, root, context).GetAwaiter().GetResult();
    }

    private sealed class NodeShouldEmitTap(string module, HookScope scope, IHookRunner runner, string root, int stage)
        : IAsyncBailHookTap<CompilationContext, bool>
    {
        public int Stage => stage;

        public async Task<bool> RunAsync(CompilationContext context)
        {
            // A hook that doesn't apply to this build never vetoes emit.
            if (!ModeApplies(scope.Mode, context.IsDevelopment))
            {
                return false;
            }

            var result = await runner.RunAsync(module, new HookInvocation
            {
                Hook = "shouldEmit",
                Root = root,
                Dev = context.IsDevelopment,
                Options = scope.Options,
                Name = scope.Name,
            });

            // The bail hook short-circuits on a non-default (true) result; a module
            // returning { emit: false } vetoes writing.
            return result?.Emit == false;
        }
    }
}
