namespace NetPack.Config;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Loads and merges netpack presets (see <see cref="PresetConfig"/>). Presets and
/// hook modules are located with the same lightweight resolution used elsewhere:
/// a relative/absolute reference resolves as a file; a bare/scoped package
/// reference resolves through <c>node_modules</c> (a subpath directly, or the
/// package's <c>main</c>). Presets must resolve to a JSON file; hook references
/// to a JS module.
///
/// Precedence is first-write-wins: options are read in order — highest priority
/// first (CLI &gt; each <c>--preset</c> &gt; auto <c>netpack.json</c>, each
/// followed by its referenced presets, depth-first) — and the first value seen
/// for an option sticks. Hooks are the mirror image: they accumulate across every
/// preset and run <b>base-first</b> (the deepest referenced presets execute
/// before the ones that pulled them in), deduplicated by resolved path.
/// </summary>
public static class Presets
{
    /// <summary>The config file auto-discovered in the working directory.</summary>
    public const string DefaultFileName = "netpack.json";

    private static readonly string[] HookExtensions = [".js", ".mjs", ".cjs", ".json"];
    private static readonly string[] PresetExtensions = [".json"];

    private static readonly JsonDocumentOptions LenientJson = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Resolves a preset chain. <paramref name="entryReferences"/> are the
    /// top-level references in precedence order (highest first). Relative
    /// references resolve against <paramref name="baseDir"/>.
    /// </summary>
    public static ResolvedPresets Resolve(IEnumerable<string> entryReferences, string baseDir)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<(PresetConfig Config, string Dir, string Path)>();

        void Load(string reference, string fromDir)
        {
            var path = ResolveModule(reference, fromDir, PresetExtensions)
                ?? throw new InvalidOperationException(
                    $"Could not resolve preset '{reference}' from '{fromDir}'.");

            // A preset already pulled in earlier keeps its earlier (higher) place
            // and is not visited again — this is also what breaks reference cycles.
            if (!visited.Add(path))
            {
                return;
            }

            var config = Parse(path);
            var dir = Path.GetDirectoryName(path)!;
            ordered.Add((config, dir, path));

            foreach (var child in config.Presets ?? [])
            {
                Load(child, dir);
            }
        }

        foreach (var reference in entryReferences)
        {
            Load(reference, baseDir);
        }

        return new ResolvedPresets(
            Merge(ordered.Select(o => o.Config)),
            ResolveHooks(ordered),
            ordered.Select(o => o.Path).ToList());
    }

    /// <summary>First-write-wins across the chain: each option takes the value
    /// from the earliest (highest-priority) preset that sets it.</summary>
    private static PresetConfig Merge(IEnumerable<PresetConfig> configs)
    {
        var result = new PresetConfig();

        foreach (var c in configs)
        {
            result.OutDir ??= c.OutDir;
            result.Minify ??= c.Minify;
            result.SourceMap ??= c.SourceMap;
            result.Clean ??= c.Clean;
            result.External ??= c.External;
            result.Shared ??= c.Shared;
            result.Format ??= c.Format;
            result.Platform ??= c.Platform;
            result.Define ??= c.Define;
            result.Alias ??= c.Alias;
            result.Loader ??= c.Loader;
            result.EntryNames ??= c.EntryNames;
            result.PublicPath ??= c.PublicPath;
            result.Conditions ??= c.Conditions;
            result.Packages ??= c.Packages;
            result.Banner ??= c.Banner;
            result.Port ??= c.Port;
            result.SplitChunks ??= c.SplitChunks;

            // Merge variants: same-name variants from different presets combine
            // their overrides (later wins on conflicts per field).
            if (c.Variants is not null)
            {
                result.Variants ??= [];

                foreach (var (name, variant) in c.Variants)
                {
                    if (result.Variants.TryGetValue(name, out var existing))
                    {
                        result.Variants[name] = MergeVariant(existing, variant);
                    }
                    else
                    {
                        result.Variants[name] = variant;
                    }
                }
            }
        }

        return result;
    }

    public static BasePresetConfig MergeVariant(BasePresetConfig base_, BasePresetConfig overrides)
    {
        return new BasePresetConfig
        {
            OutDir = overrides.OutDir ?? base_.OutDir,
            Minify = overrides.Minify ?? base_.Minify,
            SourceMap = overrides.SourceMap ?? base_.SourceMap,
            Clean = overrides.Clean ?? base_.Clean,
            External = overrides.External ?? base_.External,
            Shared = overrides.Shared ?? base_.Shared,
            Format = overrides.Format ?? base_.Format,
            Platform = overrides.Platform ?? base_.Platform,
            Define = overrides.Define ?? base_.Define,
            Alias = overrides.Alias ?? base_.Alias,
            Loader = overrides.Loader ?? base_.Loader,
            EntryNames = overrides.EntryNames ?? base_.EntryNames,
            PublicPath = overrides.PublicPath ?? base_.PublicPath,
            Conditions = overrides.Conditions ?? base_.Conditions,
            Packages = overrides.Packages ?? base_.Packages,
            Banner = overrides.Banner ?? base_.Banner,
            Port = overrides.Port ?? base_.Port,
            SplitChunks = overrides.SplitChunks ?? base_.SplitChunks,
        };
    }

    /// <summary>
    /// Collects hooks base-first (reverse of option precedence) so the deepest
    /// presets run first; a preset's own list keeps its authored order. Duplicate
    /// module paths (the same hook reached through two presets) run once.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ResolveHooks(
        List<(PresetConfig Config, string Dir, string Path)> ordered)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var (config, dir, _) = ordered[i];

            if (config.Hooks is null)
            {
                continue;
            }

            foreach (var (name, references) in config.Hooks)
            {
                if (references is null)
                {
                    continue;
                }

                foreach (var reference in references)
                {
                    var path = ResolveModule(reference, dir, HookExtensions)
                        ?? throw new InvalidOperationException(
                            $"Could not resolve hook module '{reference}' for hook '{name}' from '{dir}'.");

                    if (!seen.TryGetValue(name, out var set))
                    {
                        seen[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    if (!result.TryGetValue(name, out var list))
                    {
                        result[name] = list = [];
                    }

                    if (set.Add(path))
                    {
                        list.Add(path);
                    }
                }
            }
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.Ordinal);
    }

    private static PresetConfig Parse(string path)
    {
        var json = File.ReadAllText(path);

        try
        {
            var config = JsonSerializer.Deserialize(json, ConfigSourceGenerationContext.Default.PresetConfig)
                ?? new PresetConfig();

            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid preset JSON at '{path}': {ex.Message}");
        }
    }

    // -- resolution ---------------------------------------------------------

    /// <summary>Resolves a preset/hook reference to an absolute file path, or null
    /// if nothing matches. <paramref name="extensions"/> are the extensions tried
    /// when the reference has none.</summary>
    private static string? ResolveModule(string reference, string fromDir, string[] extensions)
    {
        if (reference.StartsWith('.') || Path.IsPathRooted(reference))
        {
            var full = Path.GetFullPath(Path.Combine(fromDir, reference));
            return ResolveFile(full, extensions);
        }

        var (package, subpath) = SplitPackage(reference);
        string? dir = fromDir;

        while (dir is not null)
        {
            var packageDir = Path.Combine(dir, "node_modules", package);

            if (Directory.Exists(packageDir))
            {
                if (subpath is not null)
                {
                    var resolved = ResolveFile(Path.Combine(packageDir, subpath), extensions);

                    if (resolved is not null)
                    {
                        return resolved;
                    }
                }
                else
                {
                    var main = ReadPackageMain(Path.Combine(packageDir, "package.json"));
                    var resolved = main is not null
                        ? ResolveFile(Path.Combine(packageDir, main), extensions)
                        : null;

                    return resolved ?? ResolveFile(Path.Combine(packageDir, "index"), extensions);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static string? ResolveFile(string path, string[] extensions)
    {
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        foreach (var extension in extensions)
        {
            var trial = path + extension;

            if (File.Exists(trial))
            {
                return Path.GetFullPath(trial);
            }
        }

        if (Directory.Exists(path))
        {
            return ResolveFile(Path.Combine(path, "index"), extensions);
        }

        return null;
    }

    private static (string Package, string? Subpath) SplitPackage(string reference)
    {
        var parts = reference.Split('/');

        if (reference.StartsWith('@'))
        {
            // Scoped: @scope/name[/subpath…]
            if (parts.Length >= 2)
            {
                var package = $"{parts[0]}/{parts[1]}";
                var subpath = parts.Length > 2 ? string.Join('/', parts.Skip(2)) : null;
                return (package, subpath);
            }

            return (reference, null);
        }

        var name = parts[0];
        var sub = parts.Length > 1 ? string.Join('/', parts.Skip(1)) : null;
        return (name, sub);
    }

    private static string? ReadPackageMain(string packageJsonPath)
    {
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath), LenientJson);

            if (doc.RootElement.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.String)
            {
                return main.GetString();
            }
        }
        catch (JsonException)
        {
            // A malformed package.json shouldn't crash preset resolution.
        }

        return null;
    }
}
