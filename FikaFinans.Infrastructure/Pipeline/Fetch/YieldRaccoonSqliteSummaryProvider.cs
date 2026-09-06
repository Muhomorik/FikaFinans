using System.Diagnostics;

using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.YieldRaccoon;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.YieldRaccoon;

using Microsoft.Data.Sqlite;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Fetch;

/// <summary>
/// Local <see cref="IFundSummaryProvider"/> that windows one fund's NAV history
/// out of YieldRacoon's database, opened <b>read-only</b>. The local stand-in for
/// YR's per-ISIN HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Reuses the mirrored pieces in <c>YieldRaccoon\</c> rather than re-deriving the
/// math: this is the per-fund body of
/// <see cref="FundStatisticsCsvExportService.ComputeAsync"/> run for a single
/// ISIN, so the buckets match YR's own summary export by construction.
/// </para>
/// <para>
/// YR's <c>FundHistoryRecords</c> holds live history, not per-week snapshots, so
/// <c>isoWeek</c> is accepted for contract compatibility and ignored. <c>company</c>
/// is likewise unused — the caller's metadata read already decides what is in
/// scope, and buckets for an ISIN with no metadata are dropped at join time.
/// </para>
/// </remarks>
public sealed class YieldRaccoonSqliteSummaryProvider : IFundSummaryProvider
{
    /// <summary>
    /// Window width behind the <c>*_2w_*</c> fields on <see cref="NavBucket"/>.
    /// Changing it would rename the metrics, not just retune them.
    /// </summary>
    private const int SummaryWindowSizeDays = 14;

    private readonly ILogger _logger;
    private readonly string _dbPath;

    public YieldRaccoonSqliteSummaryProvider(ILogger logger, NavSyncOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _dbPath = options.YieldRaccoonDbPath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavBucket>> GetNavBucketsAsync(
        Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default)
    {
        // No path configured yet (Settings not filled in) → no buckets, so the
        // caller sees a fund with no history rather than a missing-file throw.
        if (string.IsNullOrWhiteSpace(_dbPath))
        {
            _logger.Trace("YR summary read skipped — no database path configured");
            return Array.Empty<NavBucket>();
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.Trace("YR summary read starting — isin={0} db={1}", isin.Value, _dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            // Don't hold a pooled handle on YR's file between reads.
            Pooling = false
        }.ConnectionString;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var navSeries = await FundQueryHelpers
            .ReadNavSeriesAsync(connection, isin.Value, cutoffDate: null)
            .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        if (navSeries.Count < 2)
        {
            _logger.Trace(
                "YR summary read done — isin={0} has {1} NAV point(s), no windows, {2} ms",
                isin.Value, navSeries.Count, stopwatch.ElapsedMilliseconds);
            return Array.Empty<NavBucket>();
        }

        var windows = FundStatisticsCsvExportService.SliceIntoWindows(navSeries, SummaryWindowSizeDays);
        var buckets = new List<NavBucket>(windows.Count);

        foreach (var window in windows)
        {
            var navValues = window.Select(p => p.nav).ToArray();
            // Name only ever reaches the CSV's name column and NavBucket carries no
            // identity, so it goes unread here. ISIN is the producer's own fallback
            // for a null name (see FundQueryHelpers.ReadFundProfilesAsync).
            var stats = FundStatisticsCalculator.Compute(
                isin.Value, isin.Value, window[0].date, window[^1].date, navValues);

            if (stats != null)
                buckets.Add(ToBucket(stats));
        }

        stopwatch.Stop();
        _logger.Trace(
            "YR summary read done — isin={0} produced {1} bucket(s) from {2} NAV point(s) in {3} ms",
            isin.Value, buckets.Count, navSeries.Count, stopwatch.ElapsedMilliseconds);

        return buckets;
    }

    /// <summary>
    /// Projects the producer's statistics row onto <see cref="NavBucket"/>,
    /// mirroring <c>Pipeline.Csv.SummaryCsvParser</c> field for field: only
    /// <c>sharpe_2w</c> treats NaN as null, matching the calculator's near-zero
    /// volatility guard. The remaining casts keep full <see cref="double"/>
    /// precision, where the CSV path rounds to four decimals.
    /// </summary>
    private static NavBucket ToBucket(FundSummaryStatistics s) => new()
    {
        PeriodStart        = s.PeriodStart,
        PeriodEnd          = s.PeriodEnd,
        FirstNav           = s.FirstNav,
        LastNav            = s.LastNav,
        NavHigh            = s.NavHigh,
        NavLow             = s.NavLow,
        Return2wPct        = (decimal)s.Return2wPct,
        AnnVolatility2wPct = (decimal)s.AnnVolatility2wPct,
        MaxDrawdown2wPct   = (decimal)s.MaxDrawdown2wPct,
        CurrentDrawdownPct = (decimal)s.CurrentDrawdownPct,
        Sharpe2w           = double.IsNaN(s.Sharpe2w) ? null : (decimal)s.Sharpe2w,
        BestDayPct         = (decimal)s.BestDayPct,
        WorstDayPct        = (decimal)s.WorstDayPct,
        PctPositiveDays    = (decimal)s.PctPositiveDays,
        Skewness           = (decimal)s.Skewness,
    };
}
