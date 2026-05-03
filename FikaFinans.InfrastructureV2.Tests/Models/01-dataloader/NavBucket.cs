namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class NavBucket
{
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public decimal FirstNav { get; init; }
    public decimal LastNav { get; init; }
    public decimal NavHigh { get; init; }
    public decimal NavLow { get; init; }
    public decimal Return2wPct { get; init; }
    public decimal AnnVolatility2wPct { get; init; }
    public decimal MaxDrawdown2wPct { get; init; }
    public decimal CurrentDrawdownPct { get; init; }
    public decimal? Sharpe2w { get; init; }
    public decimal BestDayPct { get; init; }
    public decimal WorstDayPct { get; init; }
    public decimal PctPositiveDays { get; init; }
    public decimal Skewness { get; init; }
}
