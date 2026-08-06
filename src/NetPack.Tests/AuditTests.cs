namespace NetPack.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetPack.Graph;
using NetPack.Graph.Writers;
using NetPack.Json;
using Xunit;

public class AuditTests
{
    [Fact]
    public void Parses_an_npm_bulk_advisory_response()
    {
        var json = """
        {
          "lodash": [
            {
              "id": 1523,
              "url": "https://npmjs.com/advisories/1523",
              "title": "Prototype Pollution",
              "severity": "high",
              "vulnerable_versions": "<4.17.11",
              "cwe": ["CWE-471"],
              "cvss": { "score": 7.4, "vectorString": "CVSS:3.0/AV:N" }
            }
          ]
        }
        """;

        var report = DependencyAudit.Parse(json, new Dictionary<string, string> { ["lodash"] = "4.17.10" });

        var vulnerability = Assert.Single(report.Vulnerabilities!);
        Assert.Equal("lodash", vulnerability.Name);
        Assert.Equal("4.17.10", vulnerability.Version);
        Assert.Equal("high", vulnerability.Severity);
        Assert.Equal("Prototype Pollution", vulnerability.Title);
        Assert.Equal("1523", vulnerability.Id);
        Assert.Equal("<4.17.11", vulnerability.VulnerableVersions);
        Assert.Equal(7.4, vulnerability.CvssScore);
        Assert.Contains("CWE-471", vulnerability.Cwe!);
        Assert.Equal(1, report.Summary!["high"]);
    }

    [Fact]
    public void Empty_response_has_no_vulnerabilities()
    {
        var report = DependencyAudit.Parse("{}", new Dictionary<string, string>());
        Assert.Empty(report.Vulnerabilities!);
    }

    [Fact]
    public async Task No_dependencies_skips_the_network_and_reports_zero()
    {
        var context = new BundlerContext("/root", FeatureFlags.None);

        var report = await DependencyAudit.RunAsync(context);

        Assert.Equal(0, report.Checked);
        Assert.Empty(report.Vulnerabilities!);
        Assert.Null(report.Error);
    }

    [Fact]
    public void Metafile_embeds_the_audit_report()
    {
        var context = new BundlerContext("/root", FeatureFlags.None);
        var audit = new AuditReport
        {
            Checked = 1,
            Vulnerabilities = [new AuditVulnerability { Name = "left-pad", Severity = "low" }],
            Summary = new() { ["low"] = 1 },
        };

        var json = Traverse.BuildMetafile(context, Array.Empty<EmittedFile>(), audit);

        Assert.Contains("\"audit\"", json);
        Assert.Contains("\"vulnerabilities\"", json);
        Assert.Contains("left-pad", json);
    }
}
