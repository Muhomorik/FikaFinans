namespace FikaFinans.Domain.Portfolio;

public sealed class CashPolicySummary
{
    public decimal FloorPct { get; init; }
    public bool RegimeOverrideActive { get; init; }
    public string? RegimeUsed { get; init; }
}
