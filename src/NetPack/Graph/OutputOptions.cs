namespace NetPack.Graph;

public record OutputOptions
{
    public required bool IsOptimizing { get; init; }

    public required bool IsReloading { get; init; }
}
