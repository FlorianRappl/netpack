namespace NetPack.Json;

using System.Text.Json.Serialization;

public sealed class ModuleFederation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "remoteEntry.js";
    
    [JsonPropertyName("shareScope")]
    public string ShareScope { get; set; } = "default";
    
    [JsonPropertyName("shareStrategy")]
    public string ShareStrategy { get; set; } = "version-first";
    
    [JsonPropertyName("manifest")]
    public bool ShouldProduceManifest { get; set; } = true;
    
    [JsonPropertyName("shared")]
    public Dictionary<string, SharedEntry>? Shared { get; set; }

    [JsonPropertyName("exposes")]
    public Dictionary<string, string>? Exposes { get; set; }

    [JsonPropertyName("remotes")]
    public Dictionary<string, RemoteEntry>? Remotes { get; set; }
}

public sealed class SharedEntry
{
    [JsonPropertyName("requiredVersion")]
    public string RequiredVersion { get; set; } = "";
    
    [JsonPropertyName("shareScope")]
    public string ShareScope { get; set; } = "default";
    
    [JsonPropertyName("singleton")]
    public bool IsSingleton { get; set; } = false;
    
    [JsonPropertyName("eager")]
    public bool IsEager { get; set; } = false;
}

public sealed class RemoteEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "module";

    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "";

    [JsonPropertyName("globalName")]
    public string GlobalName { get; set; } = "";

    [JsonPropertyName("shareScope")]
    public string ShareScope { get; set; } = "default";
}
