namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class PortfolioStructure
{
    public required IReadOnlyList<PinnedFund> Pinnings { get; init; }
}
