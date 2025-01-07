namespace NetPack.Graph;

using System.Text.Json.Serialization;

internal class Importmap
{
    [JsonPropertyName("imports")]
    public Dictionary<string, string>? Imports { get; set; }
}
