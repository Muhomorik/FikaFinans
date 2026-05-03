namespace FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;

public sealed class Metrics
{
    public int WindowsPositiveCount { get; init; }
    public int WindowsTotal { get; init; }

    public decimal? CurrentDrawdownPct { get; init; }
    public decimal? AnnVolatility2wPct { get; init; }
    public decimal? Sharpe2w { get; init; }

    public decimal? Sharpe12w { get; init; }
    public decimal? Sharpe1y { get; init; }
    public decimal? AnnVolatility12wPct { get; init; }
    public decimal? AnnVolatility1yPct { get; init; }
    public decimal? Return12wCompoundPct { get; init; }
    public decimal? Return1yCompoundPct { get; init; }
    public decimal? MaxDrawdown12wPct { get; init; }
    public decimal? MaxDrawdown1yPct { get; init; }

    public decimal TotalFeePct { get; init; }
    public decimal? NetReturnAfterFee12wPct { get; init; }

    public DateOnly? AsOfDate { get; init; }

    public required MetricsDataQuality DataQuality { get; init; }
}
