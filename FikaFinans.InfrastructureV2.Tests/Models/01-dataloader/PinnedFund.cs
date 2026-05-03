namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class PinnedFund
{
    public string? Isin { get; init; }
    public string? Name { get; init; }
    public required PinnedLayer Layer { get; init; }
}
