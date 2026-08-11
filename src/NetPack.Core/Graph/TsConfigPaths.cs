namespace NetPack.Graph;

using System.Collections.Concurrent;
using System.Text.Json;
using static NetPack.Helpers;

/// <summary>
/// Resolves the <c>tsconfig.json</c> that governs a given directory — the closest
/// one walking upward — and caches the result so a monorepo with many per-package
/// tsconfigs works while each file is still parsed at most once. The directory→
/// mapping walk is memoized too, so repeated misses from the same folder are free.
/// </summary>
internal sealed class TsConfigResolver
{
    // tsconfig.json path -> its parsed mapping (null when it declares no usable paths).
    private readonly ConcurrentDictionary<string, TsConfigPaths?> _byFile = new(StringComparer.Ordinal);

    // directory -> the applicable mapping (memoized walk-up; null when none applies).
    private readonly ConcurrentDictionary<string, TsConfigPaths?> _byDirectory = new(StringComparer.Ordinal);

    /// <summary>The path mapping that applies to files in
    /// <paramref name="directory"/>, or null when no ancestor tsconfig declares one.</summary>
    public TsConfigPaths? ForDirectory(string? directory)
        => string.IsNullOrEmpty(directory) ? null : _byDirectory.GetOrAdd(directory, WalkUp);

    private TsConfigPaths? WalkUp(string directory)
    {
        var current = directory;

        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "tsconfig.json");

            if (File.Exists(candidate))
            {
                var mapping = _byFile.GetOrAdd(candidate, TsConfigPaths.Load);

                // The closest tsconfig that actually declares "paths" wins. A
                // tsconfig without usable paths doesn't stop the walk — that keeps
                // the common "package tsconfig extends the root" monorepo layout
                // working, where the paths live in the root config.
                if (mapping is not null)
                {
                    return mapping;
                }
            }

            var parent = Path.GetDirectoryName(current);

            if (parent == current)
            {
                break;
            }

            current = parent;
        }

        return null;
    }
}

/// <summary>
/// TypeScript path mapping (<c>compilerOptions.paths</c> + <c>baseUrl</c>) parsed
/// from a single <c>tsconfig.json</c>. Used purely as a resolution fallback: when
/// a specifier such as <c>@/components</c> doesn't resolve normally, we try the
/// aliases declared here.
/// </summary>
internal sealed class TsConfigPaths
{
    private readonly string _baseDir;
    private readonly List<Pattern> _patterns;

    private TsConfigPaths(string baseDir, List<Pattern> patterns)
    {
        _baseDir = baseDir;
        _patterns = patterns;
    }

    /// <summary>A single <c>paths</c> entry. <see cref="Wildcard"/> mirrors a
    /// <c>*</c> in the key (<c>"@/*"</c>); otherwise it is an exact match.</summary>
    private readonly record struct Pattern(string Prefix, string Suffix, bool Wildcard, string[] Targets);

    /// <summary>
    /// Yields candidate absolute paths for <paramref name="specifier"/>, using the
    /// TypeScript rule that the pattern with the longest literal prefix wins.
    /// Empty when nothing matches (so the caller can fall through).
    /// </summary>
    public IEnumerable<string> Resolve(string specifier)
    {
        Pattern? best = null;
        var bestLength = -1;

        foreach (var pattern in _patterns)
        {
            if (pattern.Wildcard)
            {
                if (specifier.Length >= pattern.Prefix.Length + pattern.Suffix.Length &&
                    specifier.StartsWith(pattern.Prefix, StringComparison.Ordinal) &&
                    specifier.EndsWith(pattern.Suffix, StringComparison.Ordinal) &&
                    pattern.Prefix.Length > bestLength)
                {
                    best = pattern;
                    bestLength = pattern.Prefix.Length;
                }
            }
            else if (specifier == pattern.Prefix && pattern.Prefix.Length > bestLength)
            {
                best = pattern;
                bestLength = pattern.Prefix.Length;
            }
        }

        if (best is null)
        {
            yield break;
        }

        var matched = best.Value.Wildcard
            ? specifier.Substring(best.Value.Prefix.Length, specifier.Length - best.Value.Prefix.Length - best.Value.Suffix.Length)
            : null;

        foreach (var target in best.Value.Targets)
        {
            var replaced = matched is null ? target : target.Replace("*", matched);
            yield return CombinePath(_baseDir, replaced);
        }
    }

    /// <summary>
    /// Parses the path mapping from the <c>tsconfig.json</c> at
    /// <paramref name="file"/>. Returns null when it declares no <c>paths</c> or is
    /// unreadable — path mapping is a convenience, never a hard failure.
    /// </summary>
    public static TsConfigPaths? Load(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (!doc.RootElement.TryGetProperty("compilerOptions", out var compilerOptions) ||
                compilerOptions.ValueKind != JsonValueKind.Object ||
                !compilerOptions.TryGetProperty("paths", out var paths) ||
                paths.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // "paths" are resolved relative to "baseUrl" (relative to the tsconfig
            // directory); modern TS also allows "paths" without "baseUrl", in which
            // case they are relative to the tsconfig directory itself.
            var tsconfigDir = Path.GetDirectoryName(file)!;
            var baseUrl = compilerOptions.TryGetProperty("baseUrl", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString()!
                : ".";
            var baseDir = CombinePath(tsconfigDir, baseUrl);

            var patterns = new List<Pattern>();

            foreach (var entry in paths.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var targets = new List<string>();
                foreach (var target in entry.Value.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.String)
                    {
                        targets.Add(target.GetString()!);
                    }
                }

                if (targets.Count == 0)
                {
                    continue;
                }

                var key = entry.Name;
                var star = key.IndexOf('*');

                patterns.Add(star >= 0
                    ? new Pattern(key[..star], key[(star + 1)..], true, [.. targets])
                    : new Pattern(key, string.Empty, false, [.. targets]));
            }

            return patterns.Count > 0 ? new TsConfigPaths(baseDir, patterns) : null;
        }
        catch
        {
            return null;
        }
    }
}
