namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class CapitalSummary
{
    public decimal PortfolioValueKr { get; init; }
    public decimal ActivePositionsValueKr { get; init; }
    public decimal FrozenPositionsValueKr { get; init; }
    public decimal CashAvailableKr { get; init; }
    public decimal SellProceedsKr { get; init; }
    public decimal TotalDeployableKr { get; init; }
    public decimal TotalBuyAmountKr { get; init; }
    public decimal CashRemainingKr { get; init; }
    public decimal CashFloorKr { get; init; }
    public decimal CashAboveFloorKr { get; init; }
    public required CashPolicySummary CashPolicy { get; init; }
}
