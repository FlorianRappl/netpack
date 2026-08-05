namespace NetPack.Graph;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>One collected third-party license (a package's declared license, or a
/// legal comment preserved from source).</summary>
public sealed class LicenseEntry
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("license")] public string? License { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }
}

/// <summary>
/// Collects license/legal information for a build: the legal comments preserved in
/// module source (for the <c>preamble</c> mode) and the declared licenses of the
/// resolved dependencies (for the <c>json</c>/<c>spdx</c> manifests).
/// </summary>
public static class LicenseCollector
{
    private static readonly string[] LicenseFileNames =
    [
        "LICENSE", "LICENSE.md", "LICENSE.txt", "LICENCE", "LICENCE.md", "license", "license.md",
    ];

    // -- legal comments (preamble) -----------------------------------------

    /// <summary>
    /// Extracts legal comments from JavaScript source: block/line comments that
    /// begin with <c>!</c> (i.e. <c>/*! … */</c>, <c>//! …</c>) or contain
    /// <c>@license</c>, <c>@preserve</c> or <c>@copyright</c> — the same set
    /// bundlers preserve. String and template literals are skipped so
    /// comment-looking text inside them is ignored.
    /// </summary>
    public static IReadOnlyList<string> ExtractLegalComments(string source)
    {
        var result = new List<string>();
        int i = 0, n = source.Length;

        while (i < n)
        {
            var c = source[i];

            if (c is '"' or '\'' or '`')
            {
                i = SkipString(source, i, c);
                continue;
            }

            if (c == '/' && i + 1 < n)
            {
                var d = source[i + 1];

                if (d == '*')
                {
                    var start = i;
                    i += 2;
                    while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                    {
                        i++;
                    }
                    var end = Math.Min(i + 2, n);
                    var comment = source[start..end];
                    if (IsLegal(comment.Length >= 4 ? comment[2..^2] : ""))
                    {
                        result.Add(comment);
                    }
                    i = end;
                    continue;
                }

                if (d == '/')
                {
                    var start = i;
                    i += 2;
                    while (i < n && source[i] != '\n')
                    {
                        i++;
                    }
                    var comment = source[start..i];
                    if (IsLegal(comment.Length >= 2 ? comment[2..] : ""))
                    {
                        result.Add(comment);
                    }
                    continue;
                }
            }

            i++;
        }

        return result;
    }

    private static int SkipString(string s, int i, char quote)
    {
        var n = s.Length;
        i++; // opening quote

        while (i < n)
        {
            var c = s[i];
            if (c == '\\') { i += 2; continue; }
            if (c == quote) { return i + 1; }
            i++;
        }

        return n;
    }

    private static bool IsLegal(string inner)
        => inner.StartsWith('!')
            || inner.Contains("@license", StringComparison.OrdinalIgnoreCase)
            || inner.Contains("@preserve", StringComparison.OrdinalIgnoreCase)
            || inner.Contains("@copyright", StringComparison.OrdinalIgnoreCase);

    // -- package manifest (json / spdx) ------------------------------------

    /// <summary>Renders the license manifest for a build in the requested format.</summary>
    public static string Render(LicenseMode mode, BundlerContext context)
    {
        var entries = CollectPackages(context);
        return mode == LicenseMode.Spdx ? RenderSpdx(entries) : RenderJson(entries);
    }

    /// <summary>One entry per resolved dependency (deduplicated by name+version,
    /// sorted by name), with its declared license and license text when found.</summary>
    public static List<LicenseEntry> CollectPackages(BundlerContext context)
    {
        var entries = new List<LicenseEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dependency in context.Dependencies)
        {
            string name, version;

            try
            {
                name = dependency.Name;
                version = dependency.Version;
            }
            catch
            {
                continue; // a package.json without name/version — skip it
            }

            if (!seen.Add($"{name}@{version}"))
            {
                continue;
            }

            entries.Add(new LicenseEntry
            {
                Name = name,
                Version = version,
                License = dependency.License,
                Text = ReadLicenseText(dependency.Location),
            });
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return entries;
    }

    private static string? ReadLicenseText(string packageJsonLocation)
    {
        var dir = Path.GetDirectoryName(packageJsonLocation);

        if (dir is null)
        {
            return null;
        }

        foreach (var candidate in LicenseFileNames)
        {
            var path = Path.Combine(dir, candidate);

            if (File.Exists(path))
            {
                try
                {
                    return File.ReadAllText(path).Trim();
                }
                catch
                {
                    // Unreadable license file — fall through.
                }
            }
        }

        return null;
    }

    private static string RenderJson(List<LicenseEntry> entries)
        => JsonSerializer.Serialize(entries, LicenseSerializationContext.Default.ListLicenseEntry);

    private static string RenderSpdx(List<LicenseEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("SPDXVersion: SPDX-2.3\n");
        sb.Append("DataLicense: CC0-1.0\n");
        sb.Append("SPDXID: SPDXRef-DOCUMENT\n");
        sb.Append("DocumentName: netpack-licenses\n");
        sb.Append("DocumentNamespace: https://netpack.anglevisions.com/spdx/").Append(Guid.NewGuid().ToString("N")).Append('\n');
        sb.Append("Creator: Tool: netpack\n");
        sb.Append("Created: ").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append('\n');

        foreach (var entry in entries)
        {
            sb.Append('\n');
            sb.Append("PackageName: ").Append(entry.Name).Append('\n');
            sb.Append("SPDXID: SPDXRef-Package-").Append(SpdxId(entry.Name)).Append('\n');

            if (entry.Version is not null)
            {
                sb.Append("PackageVersion: ").Append(entry.Version).Append('\n');
            }

            sb.Append("PackageDownloadLocation: NOASSERTION\n");
            sb.Append("PackageLicenseConcluded: ").Append(entry.License ?? "NOASSERTION").Append('\n');
            sb.Append("PackageLicenseDeclared: ").Append(entry.License ?? "NOASSERTION").Append('\n');
        }

        return sb.ToString();
    }

    private static string SpdxId(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "unknown";
        }

        var sb = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-');
        }

        return sb.ToString();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<LicenseEntry>))]
internal partial class LicenseSerializationContext : JsonSerializerContext
{
}
