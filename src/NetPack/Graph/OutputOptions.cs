namespace NetPack.Graph;

public record OutputOptions
{
    public required bool IsOptimizing { get; init; }

    public required bool IsReloading { get; init; }

    /// <summary>Emit a Source Map v3 next to each JS bundle and a
    /// <c>sourceMappingURL</c> comment pointing at it.</summary>
    public bool WithSourceMaps { get; init; }
}
