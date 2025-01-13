namespace NetPack.Json;

using System.Text.Json.Serialization;

public sealed class NodesDefinition
{
    [JsonPropertyName("entries")]
    public List<string> Entries { get; set; } = [];

    [JsonPropertyName("nodes")]
    public List<UnresolvedNode> Nodes { get; set; } = [];
}

public sealed class UnresolvedNode
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("bytes")]
    public int? Bytes { get; set; }

    [JsonPropertyName("children")]
    public List<string> ChildNames { get; set; } = [];

    [JsonPropertyName("references")]
    public List<string> ReferenceNames { get; set; } = [];
}
