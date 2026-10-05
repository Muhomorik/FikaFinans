namespace FikaFinans.Domain.Funds;

public sealed class PortfolioStructure
{
    public required IReadOnlyList<PinnedFund> PinnedFunds { get; init; }
}
