namespace NetPack.Graph;

using System.Text.Json;

using static NetPack.Helpers;

public sealed class Dependency(string location, JsonElement meta, bool useBrowserField = true)
{
    private readonly JsonElement sideEffects = meta.TryGetProperty("sideEffects", out var element) ? element : default;

    public string Location => location;

    public string Name => meta.GetProperty("name").GetString()!;

    public string Version => meta.GetProperty("version").GetString()!;

    public string Entry => CombinePath(Path.GetDirectoryName(location)!, GetEntry(meta, useBrowserField));

    /// <summary>
    /// True when the package declares an <c>exports</c> field. When present it is
    /// authoritative: only the subpaths it lists are importable, and the legacy
    /// <c>main</c>/<c>module</c>/<c>browser</c> fields are ignored for resolution.
    /// </summary>
    public bool HasExports => meta.TryGetProperty("exports", out var exports) &&
        exports.ValueKind is JsonValueKind.Object or JsonValueKind.String or JsonValueKind.Array;

    /// <summary>
    /// Resolves a subpath through the package's <c>exports</c> field. Pass
    /// <c>"."</c> for the package root or <c>"./sub"</c> for a subpath import.
    /// <paramref name="conditions"/> are the active condition names (see
    /// <see cref="PlatformTarget.Conditions"/>); <c>default</c> always matches.
    /// Returns an absolute file path, or null when the subpath is not exported.
    /// </summary>
    public string? ResolveExport(string subpath, IReadOnlyList<string> conditions)
    {
        if (!meta.TryGetProperty("exports", out var exports))
        {
            return null;
        }

        var target = ResolveExports(exports, subpath, conditions);

        // Targets must be package-relative ("./..."); anything else (a bare
        // specifier or a blocked null) is not a file we can resolve here.
        if (target is null || !target.StartsWith("./", StringComparison.Ordinal))
        {
            return null;
        }

        return CombinePath(Path.GetDirectoryName(location)!, target);
    }

    private static string? ResolveExports(JsonElement exports, string subpath, IReadOnlyList<string> conditions)
    {
        // A map whose keys start with "." lists subpaths; otherwise the value is
        // shorthand for the "." entry (a string, a fallback array, or conditions).
        if (exports.ValueKind == JsonValueKind.Object && IsSubpathMap(exports))
        {
            return ResolveSubpathMap(exports, subpath, conditions);
        }

        return subpath == "." ? ResolveTarget(exports, conditions, null) : null;
    }

    private static bool IsSubpathMap(JsonElement obj)
    {
        // Node forbids mixing "." keys with condition keys, so the first key
        // decides which kind of map this is.
        foreach (var prop in obj.EnumerateObject())
        {
            return prop.Name.StartsWith('.');
        }

        return false;
    }

    private static string? ResolveSubpathMap(JsonElement map, string subpath, IReadOnlyList<string> conditions)
    {
        // An exact subpath key wins over any pattern.
        if (map.TryGetProperty(subpath, out var exact) && !subpath.Contains('*'))
        {
            var resolved = ResolveTarget(exact, conditions, null);

            if (resolved is not null)
            {
                return resolved;
            }
        }

        // Otherwise the "*" pattern with the longest literal prefix matches.
        var bestPrefix = null as string;
        var bestSuffix = null as string;
        var bestTarget = default(JsonElement);

        foreach (var prop in map.EnumerateObject())
        {
            var key = prop.Name;
            var star = key.IndexOf('*');

            if (star < 0)
            {
                continue;
            }

            var prefix = key[..star];
            var suffix = key[(star + 1)..];

            if (subpath.Length >= prefix.Length + suffix.Length &&
                subpath.StartsWith(prefix, StringComparison.Ordinal) &&
                subpath.EndsWith(suffix, StringComparison.Ordinal) &&
                (bestPrefix is null || prefix.Length > bestPrefix.Length))
            {
                bestPrefix = prefix;
                bestSuffix = suffix;
                bestTarget = prop.Value;
            }
        }

        if (bestPrefix is not null)
        {
            var match = subpath.Substring(bestPrefix.Length, subpath.Length - bestPrefix.Length - bestSuffix!.Length);
            return ResolveTarget(bestTarget, conditions, match);
        }

        return null;
    }

    private static string? ResolveTarget(JsonElement target, IReadOnlyList<string> conditions, string? patternMatch)
    {
        switch (target.ValueKind)
        {
            case JsonValueKind.String:
                var value = target.GetString()!;
                return patternMatch is null ? value : value.Replace("*", patternMatch);

            case JsonValueKind.Object:
                // Conditions are tried in declaration order; the first that is
                // active (or "default") and resolves to a target wins.
                foreach (var prop in target.EnumerateObject())
                {
                    if (prop.Name == "default" || conditions.Contains(prop.Name))
                    {
                        var resolved = ResolveTarget(prop.Value, conditions, patternMatch);

                        if (resolved is not null)
                        {
                            return resolved;
                        }
                    }
                }

                return null;

            case JsonValueKind.Array:
                // A fallback list: the first entry that resolves wins.
                foreach (var item in target.EnumerateArray())
                {
                    var resolved = ResolveTarget(item, conditions, patternMatch);

                    if (resolved is not null)
                    {
                        return resolved;
                    }
                }

                return null;

            default:
                // null explicitly blocks a subpath.
                return null;
        }
    }

    public bool HasSideEffects(string file)
    {
        if (sideEffects.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        else if (sideEffects.ValueKind == JsonValueKind.False)
        {
            return false;
        }
        else if (sideEffects.ValueKind == JsonValueKind.Array)
        {
            var parentDir = Path.GetDirectoryName(location)!;

            foreach (var entry in sideEffects.EnumerateArray())
            {
                var str = entry.GetString();

                if (str is not null)
                {
                    var path = Path.Combine(parentDir, str);

                    if (file == path)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return true;
    }

    private static string GetEntry(JsonElement jsonObj, bool useBrowserField)
    {
        if (useBrowserField && jsonObj.TryGetProperty("browser", out var browserProperty) && browserProperty.ValueKind == JsonValueKind.String)
        {
            return browserProperty.GetString()!;
        }

        if (jsonObj.TryGetProperty("source", out var sourceProperty) && sourceProperty.ValueKind == JsonValueKind.String)
        {
            return sourceProperty.GetString()!;
        }

        if (jsonObj.TryGetProperty("module", out var moduleProperty) && moduleProperty.ValueKind == JsonValueKind.String)
        {
            return moduleProperty.GetString()!;
        }

        if (jsonObj.TryGetProperty("main", out var mainProperty) && mainProperty.ValueKind == JsonValueKind.String)
        {
            return mainProperty.GetString()!;
        }

        return "index.js";
    }
}
