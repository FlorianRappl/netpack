namespace NetPack.Config;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>Maps to webpack/rspack's <c>optimization.splitChunks</c>.</summary>
public sealed class SplitChunksConfig
{
    [JsonPropertyName("chunks")]
    public string? Chunks { get; set; }

    [JsonPropertyName("minSize")]
    public int? MinSize { get; set; }

    [JsonPropertyName("minSizeReduction")]
    public int? MinSizeReduction { get; set; }

    [JsonPropertyName("minChunks")]
    public int? MinChunks { get; set; }

    [JsonPropertyName("maxAsyncRequests")]
    public int? MaxAsyncRequests { get; set; }

    [JsonPropertyName("maxInitialRequests")]
    public int? MaxInitialRequests { get; set; }

    [JsonPropertyName("maxSize")]
    public int? MaxSize { get; set; }

    [JsonPropertyName("enforceSizeThreshold")]
    public int? EnforceSizeThreshold { get; set; }

    [JsonPropertyName("cacheGroups")]
    public Dictionary<string, CacheGroupConfig>? CacheGroups { get; set; }
}

public sealed class CacheGroupConfig
{
    [JsonPropertyName("test")]
    public string? Test { get; set; }

    [JsonPropertyName("chunks")]
    public string? Chunks { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("enforce")]
    public bool? Enforce { get; set; }

    [JsonPropertyName("minChunks")]
    public int? MinChunks { get; set; }

    [JsonPropertyName("minSize")]
    public int? MinSize { get; set; }

    [JsonPropertyName("maxSize")]
    public int? MaxSize { get; set; }

    [JsonPropertyName("reuseExistingChunk")]
    public bool? ReuseExistingChunk { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SplitChunksConfig))]
[JsonSerializable(typeof(CacheGroupConfig))]
[JsonSerializable(typeof(Dictionary<string, CacheGroupConfig>))]
internal partial class SplitChunksSourceGenerationContext : JsonSerializerContext
{
}
