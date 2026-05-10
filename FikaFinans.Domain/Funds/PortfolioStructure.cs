namespace FikaFinans.Domain.Funds;

public sealed class PortfolioStructure
{
    public required IReadOnlyList<PinnedFund> Pinnings { get; init; }
}
