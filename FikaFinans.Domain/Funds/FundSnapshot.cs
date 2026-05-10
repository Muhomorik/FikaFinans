namespace FikaFinans.Domain.Funds;

public sealed class FundSnapshot
{
    public DateOnly AsOfDate { get; init; }
    public decimal? Return12wCompoundPct { get; init; }
    public decimal? AnnVolatility12wPct { get; init; }
    public decimal? Sharpe12w { get; init; }
    public decimal? MaxDrawdown12wPct { get; init; }
    public decimal? Return1yCompoundPct { get; init; }
    public decimal? AnnVolatility1yPct { get; init; }
    public decimal? Sharpe1y { get; init; }
    public decimal? MaxDrawdown1yPct { get; init; }
}
