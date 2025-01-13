namespace NetPack.Json;

using System.Text.Json.Serialization;

public sealed class MetadataContainer
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, InputNode>? Inputs { get; set; }

    [JsonPropertyName("outputs")]
    public Dictionary<string, OutputNode>? Outputs { get; set; }
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
