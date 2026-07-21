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

public sealed class SvelteCommandResult
{
    /// <summary>The compiled ES module for the component.</summary>
    [JsonPropertyName("js")]
    public string? Js { get; set; }

    /// <summary>Extracted CSS, when not injected by the compiled JS.</summary>
    [JsonPropertyName("css")]
    public string? Css { get; set; }
}
