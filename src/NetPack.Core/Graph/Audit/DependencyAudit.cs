namespace NetPack.Graph;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetPack.Json;

/// <summary>
/// Audits the packages present in the module graph against known vulnerabilities
/// using npm's public bulk advisory endpoint (the same data <c>npm audit</c> uses).
/// It sends only package names and versions — the set that actually made it into
/// the graph — and returns the raised advisories. Failures (offline, timeout, a
/// registry error) are captured on the report rather than thrown, so a diagnostic
/// run never fails the build.
/// </summary>
public static class DependencyAudit
{
    private const string Endpoint = "https://registry.npmjs.org/-/npm/v1/security/advisories/bulk";

    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, AuditReport> Cache = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "netpack");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return client;
    }

    /// <summary>Audits the graph's dependencies. Results are cached per distinct
    /// dependency set, so repeated calls (e.g. watch-mode rebuilds) don't re-query
    /// the registry unless the dependencies changed.</summary>
    public static async Task<AuditReport> RunAsync(BundlerContext context, CancellationToken cancellationToken = default)
    {
        var present = CollectPackages(context);

        if (present.Count == 0)
        {
            return new AuditReport { Checked = 0, Vulnerabilities = [], Summary = new(StringComparer.Ordinal) };
        }

        var key = string.Join(";", present.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}@{p.Value}"));

        await Gate.WaitAsync(cancellationToken);

        try
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var report = await QueryAsync(present, cancellationToken);
            Cache[key] = report;
            return report;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Distinct package name → comma-joined present versions.</summary>
    private static SortedDictionary<string, string> CollectPackages(BundlerContext context)
    {
        var versions = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

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
                continue;
            }

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
            {
                continue;
            }

            if (!versions.TryGetValue(name, out var set))
            {
                versions[name] = set = new SortedSet<string>(StringComparer.Ordinal);
            }

            set.Add(version);
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, set) in versions)
        {
            result[name] = string.Join(", ", set);
        }

        return result;
    }

    private static async Task<AuditReport> QueryAsync(SortedDictionary<string, string> present, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(BuildRequestBody(present), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(Endpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var report = Parse(json, present);
            report.Checked = present.Count;
            return report;
        }
        catch (Exception ex)
        {
            return new AuditReport
            {
                Checked = present.Count,
                Vulnerabilities = [],
                Summary = new(StringComparer.Ordinal),
                Error = ex.Message,
            };
        }
    }

    private static string BuildRequestBody(SortedDictionary<string, string> present)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var (name, joined) in present)
            {
                writer.WriteStartArray(name);

                foreach (var version in joined.Split(", ", StringSplitOptions.RemoveEmptyEntries))
                {
                    writer.WriteStringValue(version);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parses an npm bulk-advisory response (<c>{ "pkg": [ { advisory }, … ] }</c>)
    /// into an <see cref="AuditReport"/>. <paramref name="present"/> maps each
    /// package to the version(s) that are in the graph.
    /// </summary>
    internal static AuditReport Parse(string json, IReadOnlyDictionary<string, string> present)
    {
        var report = new AuditReport
        {
            Vulnerabilities = [],
            Summary = new(StringComparer.Ordinal),
        };

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return report;
        }

        foreach (var package in document.RootElement.EnumerateObject())
        {
            if (package.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            present.TryGetValue(package.Name, out var version);

            foreach (var advisory in package.Value.EnumerateArray())
            {
                var vulnerability = new AuditVulnerability { Name = package.Name, Version = version };

                if (advisory.TryGetProperty("severity", out var severity)) vulnerability.Severity = severity.GetString();
                if (advisory.TryGetProperty("title", out var title)) vulnerability.Title = title.GetString();
                if (advisory.TryGetProperty("url", out var url)) vulnerability.Url = url.GetString();
                if (advisory.TryGetProperty("vulnerable_versions", out var range)) vulnerability.VulnerableVersions = range.GetString();

                if (advisory.TryGetProperty("id", out var id))
                {
                    vulnerability.Id = id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString();
                }

                if (advisory.TryGetProperty("cwe", out var cwe) && cwe.ValueKind == JsonValueKind.Array)
                {
                    vulnerability.Cwe = cwe.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .ToList();
                }

                if (advisory.TryGetProperty("cvss", out var cvss) && cvss.ValueKind == JsonValueKind.Object
                    && cvss.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number)
                {
                    vulnerability.CvssScore = score.GetDouble();
                }

                report.Vulnerabilities.Add(vulnerability);

                var bucket = (vulnerability.Severity ?? "unknown").ToLowerInvariant();
                report.Summary[bucket] = report.Summary.GetValueOrDefault(bucket) + 1;
            }
        }

        return report;
    }
}
