namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Per-fund rolling-horizon snapshot at a single evaluation date. One row per fund in the snapshot CSV.
/// All eight metric fields may be <see cref="double.NaN"/> when the underlying NAV history is too short
/// for the horizon, or when the volatility guard suppresses an explosive Sharpe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrored from the YieldRaccoon producer</b> (<c>YieldRaccoon.Infrastructure.Services</c>).
/// Copied verbatim — keep it in sync with the original rather than editing it here.
/// </para>
/// <para>
/// Unlike <c>FikaFinans.Application.YieldRaccoon.FundSummaryStatistics</c>, this one stays
/// <c>internal</c> to Infrastructure exactly as upstream has it: no mirrored contract returns it
/// across the layer boundary, so there is nothing to reconcile when diffing against YR.
/// </para>
/// <para>
/// This is the producer-side shape: <see cref="double"/> metrics with <see cref="double.NaN"/> as
/// the "not computable" sentinel. The consumer-side counterpart is
/// <c>FikaFinans.Domain.Funds.FundSnapshot</c>, which drops <see cref="Isin"/> and models every
/// metric as a nullable <c>decimal</c>.
/// </para>
/// </remarks>
internal sealed record FundSnapshotStatistics(
    string Isin,
    DateOnly AsOfDate,
    double Return12wCompoundPct,
    double AnnVolatility12wPct,
    double Sharpe12w,
    double MaxDrawdown12wPct,
    double Return1yCompoundPct,
    double AnnVolatility1yPct,
    double Sharpe1y,
    double MaxDrawdown1yPct);
