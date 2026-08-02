namespace NetPack.Plugins;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
    /// <summary>The hook name being invoked (e.g. <c>beforeCompilation</c>).</summary>
    [JsonPropertyName("hook")] public string? Hook { get; set; }

    /// <summary>The project root directory.</summary>
    [JsonPropertyName("root")] public string? Root { get; set; }

    /// <summary>True for a development build (dev server).</summary>
    [JsonPropertyName("dev")] public bool Dev { get; set; }

    /// <summary>Emitted assets (for asset-transforming hooks). On the return value,
    /// any entry with <see cref="HookAsset.Text"/> set replaces that asset.</summary>
    [JsonPropertyName("files")] public List<HookAsset>? Files { get; set; }
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
/// by an <see cref="IHookRunner"/>. Each preset hook name maps to a lifecycle
/// point; the module list order (base-first, already deduplicated by the resolver)
/// is preserved via the tap stage.
/// </summary>
public static class PresetHooks
{
    /// <summary>The preset hook name → lifecycle mapping supported today.</summary>
    public const string BeforeCompilation = "beforeCompilation";
    public const string AfterBundling = "afterBundling";

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
                var module = paths[stage];

                switch (name)
                {
                    case BeforeCompilation:
                        hooks.Compiler.BeforeCompile.Tap(new BeforeCompilationTap(module, runner, root, stage));
                        break;
                    case AfterBundling:
                        hooks.Compiler.AfterEmit.Tap(new AfterBundlingTap(module, runner, root, stage));
                        break;
                    default:
                        Console.Error.WriteLine($"[netpack] Unknown preset hook '{name}' — ignored.");
                        break;
                }
            }
        }
    }

    /// <summary>The mutable name→bytes asset map an <see cref="AfterBundlingTap"/>
    /// reads and rewrites; the writer places it on the context before invoking.</summary>
    internal const string AssetsStateKey = "assets";

    // Text outputs a hook receives as strings (and may return rewritten); other
    // assets (images, fonts) are passed by name only.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".css", ".html", ".htm", ".json", ".map", ".svg", ".txt", ".xml",
    };

    internal static bool IsText(string name)
        => TextExtensions.Contains(System.IO.Path.GetExtension(name));

    private sealed class BeforeCompilationTap(string module, IHookRunner runner, string root, int stage)
        : IAsyncHookTap<CompilerContext>
    {
        public int Stage => stage;

        public Task RunAsync(CompilerContext context)
            => runner.RunAsync(module, new HookInvocation
            {
                Hook = BeforeCompilation,
                Root = root,
                Dev = context.IsDevelopment,
            });
    }

    private sealed class AfterBundlingTap(string module, IHookRunner runner, string root, int stage)
        : IAsyncHookTap<CompilationContext>
    {
        public int Stage => stage;

        public async Task RunAsync(CompilationContext context)
        {
            if (!context.State.TryGetValue(AssetsStateKey, out var raw) || raw is not Dictionary<string, byte[]> assets)
            {
                return;
            }

            var files = new List<HookAsset>();

            foreach (var (name, bytes) in assets)
            {
                if (IsText(name))
                {
                    files.Add(new HookAsset { Name = name, Text = Encoding.UTF8.GetString(bytes) });
                }
            }

            var result = await runner.RunAsync(module, new HookInvocation
            {
                Hook = AfterBundling,
                Root = root,
                Dev = context.IsDevelopment,
                Files = files,
            });

            if (result?.Files is null)
            {
                return;
            }

            // Apply any rewritten (or newly added) text assets back onto the map.
            foreach (var file in result.Files)
            {
                if (file.Name is not null && file.Text is not null)
                {
                    assets[file.Name] = Encoding.UTF8.GetBytes(file.Text);
                }
            }
        }
    }
}
