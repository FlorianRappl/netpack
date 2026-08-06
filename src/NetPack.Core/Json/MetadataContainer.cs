namespace NetPack.Json;

using System.Text.Json.Serialization;

public sealed class MetadataContainer
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, InputNode>? Inputs { get; set; }

    [JsonPropertyName("outputs")]
    public Dictionary<string, OutputNode>? Outputs { get; set; }

    /// <summary>Dependency vulnerability audit for the packages in the graph
    /// (populated by the <c>analyze</c> command). Null when not run.</summary>
    [JsonPropertyName("audit")]
    public AuditReport? Audit { get; set; }

    /// <summary>Bundle-shape optimization opportunities — modules duplicated
    /// across outputs, shared chunks with poor fan-out, etc. — with actionable
    /// recommendations. Always computed (it is cheap and graph-only).</summary>
    [JsonPropertyName("savings")]
    public SavingsReport? Savings { get; set; }
}

/// <summary>Potential bundle savings and the recommendations that would realize
/// them, derived purely from the chunk graph.</summary>
public sealed class SavingsReport
{
    /// <summary>Total bytes that are provably wasted today (duplicated module
    /// code across outputs). Recommendations whose net effect adds bytes in
    /// exchange for fewer requests are excluded from this figure.</summary>
    [JsonPropertyName("potentialBytes")]
    public int PotentialBytes { get; set; }

    /// <summary>The recommendations, most impactful first. Null when the bundle
    /// graph is already well shaped.</summary>
    [JsonPropertyName("recommendations")]
    public List<SavingsRecommendation>? Recommendations { get; set; }
}

/// <summary>A single, actionable bundle-optimization recommendation.</summary>
public sealed class SavingsRecommendation
{
    /// <summary>Machine-readable category: <c>duplicate-module</c>,
    /// <c>merge-orphan-chunk</c>, or <c>inline-small-chunk</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    /// <summary>Advisory weight: <c>high</c>, <c>medium</c>, or <c>low</c>.</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "low";

    /// <summary>A human-readable statement of the problem and the concrete fix.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>The source modules the recommendation concerns (relative paths).</summary>
    [JsonPropertyName("modules")]
    public List<string>? Modules { get; set; }

    /// <summary>The output bundles the recommendation concerns (file names).</summary>
    [JsonPropertyName("bundles")]
    public List<string>? Bundles { get; set; }

    /// <summary>Byte impact. Positive = bytes saved; negative = bytes added
    /// (traded for fewer requests).</summary>
    [JsonPropertyName("bytes")]
    public int Bytes { get; set; }

    /// <summary>How many HTTP requests applying the recommendation removes.</summary>
    [JsonPropertyName("requests")]
    public int Requests { get; set; }
}

/// <summary>The result of auditing the graph's dependencies against known
/// vulnerabilities (npm advisories / CVEs).</summary>
public sealed class AuditReport
{
    /// <summary>The advisory source (e.g. <c>npm</c>).</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "npm";

    /// <summary>How many distinct packages were checked.</summary>
    [JsonPropertyName("checked")]
    public int Checked { get; set; }

    /// <summary>Advisory count per severity (critical/high/moderate/low/info).</summary>
    [JsonPropertyName("summary")]
    public Dictionary<string, int>? Summary { get; set; }

    /// <summary>The raised advisories.</summary>
    [JsonPropertyName("vulnerabilities")]
    public List<AuditVulnerability>? Vulnerabilities { get; set; }

    /// <summary>Set when the audit could not be completed (offline, timeout, …);
    /// the build itself is unaffected.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>A single advisory affecting a dependency present in the graph.</summary>
public sealed class AuditVulnerability
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("severity")] public string? Severity { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonPropertyName("vulnerableVersions")] public string? VulnerableVersions { get; set; }

    [JsonPropertyName("cwe")] public List<string>? Cwe { get; set; }

    [JsonPropertyName("cvssScore")] public double? CvssScore { get; set; }
}

public sealed class InputNode
{
    [JsonPropertyName("bytes")]
    public int Bytes { get; set; }
    
    [JsonPropertyName("format")]
    public string Format { get; set; } = "cjs"; // "cjs" "esm"
    
    [JsonPropertyName("imports")]
    public List<InputImportDefinition>? Imports { get; set; }
}

public sealed class InputImportDefinition
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "import-statement"; // "import-statement" "require-call" "dynamic-import" "file-loader"

    [JsonPropertyName("original")]
    public string Original { get; set; } = "";
}

public sealed class OutputNode
{
    [JsonPropertyName("bytes")]
    public int Bytes { get; set; }
    
    [JsonPropertyName("imports")]
    public List<OutputImportDefinition>? Imports { get; set; }
    
    [JsonPropertyName("exports")]
    public List<OutputExportDefinition>? Exports { get; set; }

    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    [JsonPropertyName("flags")]
    public string? Flags { get; set; }
    
    [JsonPropertyName("inputs")]
    public Dictionary<string, InputDefinition>? Inputs { get; set; }
}

public sealed class OutputImportDefinition
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "import-statement"; // "import-statement" "require-call" "dynamic-import" "file-loader"
}

public sealed class OutputExportDefinition
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "import-statement"; // "import-statement" "require-call" "dynamic-import" "file-loader"
}

public sealed class InputDefinition
{
    [JsonPropertyName("bytesInOutput")]
    public int BytesInOutput { get; set; }
}
