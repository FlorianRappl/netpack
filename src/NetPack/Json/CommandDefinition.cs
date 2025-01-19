namespace NetPack.Json;

using System.Text.Json.Serialization;

public sealed class CommandDefinition
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }
}

public sealed class SassCommandResult
{
    [JsonPropertyName("css")]
    public string? Css { get; set; }
}
