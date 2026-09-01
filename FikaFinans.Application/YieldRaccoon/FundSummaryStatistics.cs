namespace FikaFinans.Application.YieldRaccoon;

/// <summary>
/// Per-bucket summary statistics computed from daily NAV data for a single fund over a single
/// bi-weekly time window. Field names use the <c>_2w</c> horizon suffix to disambiguate from
/// snapshot.csv's <c>_12w</c> / <c>_1y</c> rolling-horizon counterparts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrored from the YieldRaccoon producer</b> (<c>YieldRaccoon.Infrastructure.Services</c>).
/// Keep it in sync with the original rather than editing it here.
/// </para>
/// <para>
/// Two deliberate deviations from the upstream copy: it is <c>public</c> rather than
/// <c>internal</c>, and it lives in the Application layer — both because
/// <see cref="IFundStatisticsCsvExportService.ComputeAsync"/> returns it across the layer
/// boundary. Upstream YR keeps it internal to its Infrastructure assembly; that difference
/// has to be reconciled by hand when the in-memory overload is ported back.
/// </para>
/// <para>
/// This is the producer-side shape: it carries <see cref="Isin"/> and <see cref="Name"/> inline
/// and uses <see cref="double"/> for the nine metrics, with <see cref="double.NaN"/> as the
/// "not computable" sentinel on <see cref="Sharpe2w"/>. The consumer-side counterpart is
/// <c>FikaFinans.Domain.Funds.NavBucket</c>, which drops both identity fields and models the
/// metrics as <c>decimal</c> with a nullable Sharpe.
/// </para>
/// </remarks>
public sealed record FundSummaryStatistics(
    string Isin,
    string Name,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal FirstNav,
    decimal LastNav,
    decimal NavHigh,
    decimal NavLow,
    double Return2wPct,
    double AnnVolatility2wPct,
    double MaxDrawdown2wPct,
    double CurrentDrawdownPct,
    double Sharpe2w,
    double BestDayPct,
    double WorstDayPct,
    double PctPositiveDays,
    double Skewness);
