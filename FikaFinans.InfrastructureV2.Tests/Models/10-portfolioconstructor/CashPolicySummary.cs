namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class CashPolicySummary
{
    public decimal FloorPct { get; init; }
    public bool RegimeOverrideActive { get; init; }
    public string? RegimeUsed { get; init; }
}
