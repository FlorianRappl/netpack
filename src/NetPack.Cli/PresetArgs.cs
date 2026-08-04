namespace NetPack;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NetPack.Config;

/// <summary>
/// Applies netpack presets at the CLI boundary. Before the verb parser runs it
/// resolves any <c>--preset</c> references plus an auto-discovered
/// <c>netpack.json</c> in the working directory, then injects the merged options
/// into the argument list — but only for options the user did not pass explicitly,
/// so real CLI flags always win. Working at the argv level keeps the command
/// classes untouched and gives the documented precedence for free.
///
/// Hooks declared by the presets are resolved (to absolute module paths, base
/// first, deduplicated) and held in <see cref="Hooks"/> for a future executor;
/// nothing is invoked here.
/// </summary>
static class PresetArgs
{
    /// <summary>The options each preset-aware verb actually defines, so we never
    /// inject a flag a command doesn't have (which the parser would reject).</summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedByVerb = new(StringComparer.Ordinal)
    {
        ["bundle"] = new(StringComparer.Ordinal)
        {
            "outdir", "minify", "sourcemap", "clean", "external", "shared", "format",
            "platform", "define", "alias", "loader", "entry-names", "public-path",
            "conditions", "packages", "banner", "licenses", "split-chunks",
        },
        ["serve"] = new(StringComparer.Ordinal)
        {
            "minify", "external", "shared", "define", "alias", "loader", "banner", "licenses", "port",
        },
        ["analyze"] = new(StringComparer.Ordinal)
        {
            "external", "shared", "banner", "licenses", "port",
        },
    };

    /// <summary>Resolved hooks from the last <see cref="Apply"/> call (hook name →
    /// absolute module paths, execution order). Commands pass this into
    /// <c>Traverse.From(hookModules:)</c>, which binds them as taps executed over
    /// the Node bridge.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Hooks { get; private set; }
        = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Returns one arg set per variant when the resolved preset contains variants,
    /// or a single-item list for the normal (non-variant) path.
    /// </summary>
    public static IReadOnlyList<string[]> Apply(string[] args)
    {
        if (args.Length == 0 || !AllowedByVerb.TryGetValue(args[0], out var allowed))
        {
            return [args];
        }

        // Peel off --preset references; pass everything else through untouched.
        var passthrough = new List<string> { args[0] };
        var presetRefs = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--preset")
            {
                if (i + 1 < args.Length)
                {
                    presetRefs.Add(args[++i]);
                }
            }
            else if (arg.StartsWith("--preset=", StringComparison.Ordinal))
            {
                presetRefs.Add(arg["--preset=".Length..]);
            }
            else
            {
                passthrough.Add(arg);
            }
        }

        var baseDir = Environment.CurrentDirectory;

        // Entry references, highest priority first: each --preset, then an
        // auto-discovered netpack.json in the working directory.
        var entryRefs = new List<string>(presetRefs);
        var autoConfig = Path.Combine(baseDir, Presets.DefaultFileName);

        if (File.Exists(autoConfig))
        {
            entryRefs.Add(autoConfig);
        }

        if (entryRefs.Count == 0)
        {
            return [args];
        }

        var resolved = Presets.Resolve(entryRefs, baseDir);
        Hooks = resolved.Hooks;

        var present = PresentOptionNames(passthrough);

        // If no variants, return single arg set (original behavior).
        if (resolved.Options.Variants is null || resolved.Options.Variants.Count == 0)
        {
            var injected = new List<string>();

            foreach (var (name, tokens) in Candidates(resolved.Options))
            {
                if (allowed.Contains(name) && !present.Contains(name))
                {
                    injected.AddRange(tokens);
                }
            }

            return [[.. passthrough, .. injected]];
        }

        // Variants: one arg set per variant. CLI args that conflict with a
        // variant's overrides act as filters — only matching variants are built.
        var result = new List<string[]>();

        foreach (var (variantName, variantOptions) in resolved.Options.Variants)
        {
            if (!VariantMatchesCliArgs(variantOptions, present, passthrough, allowed))
            {
                continue;
            }

            var merged = Config.Presets.MergeVariant(resolved.Options, variantOptions);
            var injected = new List<string>();

            if (variantOptions.OutDir is null)
            {
                merged.OutDir = merged.OutDir is not null
                    ? Path.Combine(merged.OutDir, variantName)
                    : variantName;
            }

            foreach (var (name, tokens) in Candidates(merged))
            {
                if (allowed.Contains(name) && !present.Contains(name))
                {
                    injected.AddRange(tokens);
                }
            }

            result.Add([.. passthrough, .. injected]);
        }

        return result;
    }

    /// <summary>
    /// When the user explicitly passed a CLI flag that also appears in a variant's
    /// overrides, only build variants whose override matches the CLI value.
    /// Example: --platform web filters out variants with platform: node.
    /// </summary>
    private static bool VariantMatchesCliArgs(
        BasePresetConfig variant,
        HashSet<string> presentCliOptions,
        List<string> passthrough,
        HashSet<string> allowed)
    {
        // Check platform: if user passed --platform X, variant must match.
        if (presentCliOptions.Contains("platform") && variant.Platform is not null)
        {
            var cliPlatform = GetCliValue(passthrough, "--platform");
            if (cliPlatform is not null && !string.Equals(cliPlatform, variant.Platform, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (presentCliOptions.Contains("format") && variant.Format is not null)
        {
            var cliFormat = GetCliValue(passthrough, "--format");
            if (cliFormat is not null && !string.Equals(cliFormat, variant.Format, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetCliValue(List<string> args, string flag)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == flag && i + 1 < args.Count)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(flag + "=", StringComparison.Ordinal))
            {
                return args[i][(flag.Length + 1)..];
            }
        }

        return null;
    }

    private static HashSet<string> PresentOptionNames(IEnumerable<string> args)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = arg[2..];
            var eq = name.IndexOf('=');

            if (eq >= 0)
            {
                name = name[..eq];
            }

            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Turns the merged preset options into CLI tokens, grouped by option name.
    /// Boolean flags are emitted only when <c>true</c> (their CLI default is off),
    /// so a preset can enable but never spuriously disable them; a repeated/keyed
    /// option is emitted in full only when the user supplied none of its own.
    /// </summary>
    private static IEnumerable<(string Name, string[] Tokens)> Candidates(BasePresetConfig o)
    {
        if (o.OutDir is not null) yield return ("outdir", ["--outdir", o.OutDir]);
        if (o.Minify == true) yield return ("minify", ["--minify"]);
        if (o.SourceMap == true) yield return ("sourcemap", ["--sourcemap"]);
        if (o.Clean == true) yield return ("clean", ["--clean"]);
        if (o.Format is not null) yield return ("format", ["--format", o.Format]);
        if (o.Platform is not null) yield return ("platform", ["--platform", o.Platform]);
        if (o.EntryNames is not null) yield return ("entry-names", ["--entry-names", o.EntryNames]);
        if (o.PublicPath is not null) yield return ("public-path", ["--public-path", o.PublicPath]);
        if (o.Packages is not null) yield return ("packages", ["--packages", o.Packages]);
        if (o.Banner is not null) yield return ("banner", ["--banner", o.Banner]);
        if (o.Licenses is not null) yield return ("licenses", ["--licenses", o.Licenses]);
        if (o.Port is not null) yield return ("port", ["--port", o.Port.Value.ToString(CultureInfo.InvariantCulture)]);
        if (o.SplitChunks is not null) yield return ("split-chunks", ["--split-chunks", System.Text.Json.JsonSerializer.Serialize(o.SplitChunks, SplitChunksSourceGenerationContext.Default.SplitChunksConfig)]);

        if (o.External is { Count: > 0 }) yield return ("external", Repeat("--external", o.External));
        if (o.Shared is { Count: > 0 }) yield return ("shared", Repeat("--shared", o.Shared));
        if (o.Conditions is { Count: > 0 }) yield return ("conditions", Repeat("--conditions", o.Conditions));

        if (o.Define is { Count: > 0 }) yield return ("define", Pairs("--define", o.Define));
        if (o.Alias is { Count: > 0 }) yield return ("alias", Pairs("--alias", o.Alias));
        if (o.Loader is { Count: > 0 }) yield return ("loader", Pairs("--loader", o.Loader));
    }

    private static string[] Repeat(string flag, IEnumerable<string> values)
        => values.SelectMany(v => new[] { flag, v }).ToArray();

    private static string[] Pairs(string flag, IEnumerable<KeyValuePair<string, string>> map)
        => map.SelectMany(kv => new[] { flag, $"{kv.Key}={kv.Value}" }).ToArray();
}
