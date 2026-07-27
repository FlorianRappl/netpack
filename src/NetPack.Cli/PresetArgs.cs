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
            "conditions", "packages", "banner",
        },
        ["serve"] = new(StringComparer.Ordinal)
        {
            "minify", "external", "shared", "define", "alias", "loader", "banner", "port",
        },
        ["analyze"] = new(StringComparer.Ordinal)
        {
            "external", "shared", "banner", "port",
        },
    };

    /// <summary>Resolved hooks from the last <see cref="Apply"/> call (hook name →
    /// absolute module paths, execution order). Held for a future hook executor;
    /// not invoked yet.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Hooks { get; private set; }
        = new Dictionary<string, IReadOnlyList<string>>();

    public static string[] Apply(string[] args)
    {
        if (args.Length == 0 || !AllowedByVerb.TryGetValue(args[0], out var allowed))
        {
            return args;
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
            return args;
        }

        var resolved = Presets.Resolve(entryRefs, baseDir);
        Hooks = resolved.Hooks;

        var present = PresentOptionNames(passthrough);
        var injected = new List<string>();

        foreach (var (name, tokens) in Candidates(resolved.Options))
        {
            if (allowed.Contains(name) && !present.Contains(name))
            {
                injected.AddRange(tokens);
            }
        }

        return [.. passthrough, .. injected];
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
    private static IEnumerable<(string Name, string[] Tokens)> Candidates(PresetConfig o)
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
        if (o.Port is not null) yield return ("port", ["--port", o.Port.Value.ToString(CultureInfo.InvariantCulture)]);

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
