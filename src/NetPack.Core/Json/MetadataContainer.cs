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
