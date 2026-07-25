namespace NetPack.Graph;

using System.Text.RegularExpressions;

/// <summary>
/// Loads environment variables from <c>.env</c> files following the
/// dotenv convention. Supports comments, quoted values, and prefix filtering.
/// </summary>
public static partial class EnvLoader
{
    /// <summary>
    /// Parses a <c>.env</c> file content into a dictionary of key-value pairs.
    /// Lines starting with <c>#</c> are treated as comments. Values may be
    /// optionally wrapped in single or double quotes.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string content, string? prefix = null)
    {
        var result = new Dictionary<string, string>();

        foreach (var rawLine in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            // Skip comments and empty lines
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            var match = EnvLineRegex().Match(line);

            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;

            // Strip surrounding quotes if present
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            // Apply prefix filter
            if (!string.IsNullOrEmpty(prefix) && !key.StartsWith(prefix))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Loads all <c>.env</c> files from the given directory in priority order
    /// (lowest to highest): <c>.env</c>, <c>.env.local</c>,
    /// <c>.env.[mode]</c>, <c>.env.[mode].local</c>. Returns the merged
    /// result (higher-priority files override lower ones).
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadFromDirectory(string directory, string mode = "production", string? prefix = "VITE_")
    {
        var files = new[]
        {
            ".env",
            ".env.local",
            $".env.{mode}",
            $".env.{mode}.local"
        };

        var merged = new Dictionary<string, string>();

        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);

            if (!File.Exists(path))
            {
                continue;
            }

            var content = File.ReadAllText(path);
            var parsed = Parse(content, prefix);

            foreach (var (key, value) in parsed)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Generates the JavaScript code that replaces <c>import.meta.env.X</c>
    /// references with their values.
    /// </summary>
    public static string GenerateReplacements(IReadOnlyDictionary<string, string> env)
    {
        return string.Join("\n", env.Select(kv => $"import.meta.env.{kv.Key}={kv.Value}"));
    }

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)=(.*)$")]
    private static partial Regex EnvLineRegex();
}
