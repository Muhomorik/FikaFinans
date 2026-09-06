using System.Diagnostics;

using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.YieldRaccoon;

using Microsoft.Data.Sqlite;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Fetch;

/// <summary>
/// Local <see cref="IFundSnapshotProvider"/> that computes one fund's rolling-horizon
/// metrics from YieldRacoon's database, opened <b>read-only</b>. The local stand-in
/// for YR's per-ISIN HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Reuses the mirrored pieces in <c>YieldRaccoon\</c> rather than re-deriving the math:
/// this is the per-fund body of
/// <see cref="FundSnapshotCsvExportService.ExportAsync"/> run for a single ISIN, so the
/// numbers match YR's own snapshot export by construction — including <c>as_of_date</c>,
/// which anchors to the latest NAV date across the whole database, not this fund's.
/// </para>
/// <para>
/// YR's <c>FundHistoryRecords</c> holds live history, not per-week snapshots, so
/// <c>isoWeek</c> is accepted for contract compatibility and ignored. <c>company</c> is
/// likewise unused — the caller's metadata read already decides what is in scope, and a
/// snapshot for an ISIN with no metadata never reaches a fund record at join time.
/// </para>
/// </remarks>
public sealed class YieldRaccoonSqliteSnapshotProvider : IFundSnapshotProvider
{
    private readonly ILogger _logger;
    private readonly string _dbPath;

    public YieldRaccoonSqliteSnapshotProvider(ILogger logger, NavSyncOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _dbPath = options.YieldRaccoonDbPath;
    }

    /// <inheritdoc />
    public async Task<FundSnapshot?> GetSnapshotAsync(
        Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default)
    {
        // No path configured yet (Settings not filled in) → no snapshot, so the caller
        // sees the same "missing row" case the CSV path already warns about.
        if (string.IsNullOrWhiteSpace(_dbPath))
        {
            _logger.Trace("YR snapshot read skipped — no database path configured");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.Trace("YR snapshot read starting — isin={0} db={1}", isin.Value, _dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            // Don't hold a pooled handle on YR's file between reads.
            Pooling = false
        }.ConnectionString;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // Database-wide anchor, exactly as the producer's export computes it: every row in
        // one snapshot CSV shares an as_of_date, so a fund that stopped reporting shows up
        // as stale rather than silently getting its own later date.
        var asOfDate = await FundQueryHelpers.GetLatestNavDateAsync(connection).ConfigureAwait(false);
        if (asOfDate is null)
        {
            _logger.Trace("YR snapshot read done — no NAV rows in database, {0} ms", stopwatch.ElapsedMilliseconds);
            return null;
        }

        ct.ThrowIfCancellationRequested();

        var asOfValue = asOfDate.Value;
        var earliestNeeded = asOfValue.AddDays(-FundSnapshotCsvExportService.Horizon1yDays);

        var navSeries = await FundQueryHelpers
            .ReadNavSeriesAsync(connection, isin.Value, earliestNeeded)
            .ConfigureAwait(false);

        var slice12w = FundSnapshotCsvExportService.TakeHorizonSlice(
            navSeries, asOfValue, FundSnapshotCsvExportService.Horizon12wDays);
        var slice1y = FundSnapshotCsvExportService.TakeHorizonSlice(
            navSeries, asOfValue, FundSnapshotCsvExportService.Horizon1yDays);

        var stats = FundSnapshotStatisticsCalculator.Compute(isin.Value, asOfValue, slice12w, slice1y);

        stopwatch.Stop();
        _logger.Trace(
            "YR snapshot read done — isin={0} as_of={1:yyyy-MM-dd}, 12w={2} pts, 1y={3} pts, {4} ms",
            isin.Value, asOfValue, slice12w.Count, slice1y.Count, stopwatch.ElapsedMilliseconds);

        return ToSnapshot(stats);
    }

    /// <summary>
    /// Projects the producer's statistics row onto <see cref="FundSnapshot"/>, mirroring
    /// <c>Pipeline.Csv.SnapshotCsvParser</c>: every metric is nullable on both sides, so
    /// NaN maps to null throughout — short history and the near-zero volatility guard
    /// both land as "not computable". The casts keep full <see cref="double"/> precision,
    /// where the CSV path rounds to four decimals.
    /// </summary>
    private static FundSnapshot ToSnapshot(FundSnapshotStatistics s) => new()
    {
        AsOfDate             = s.AsOfDate,
        Return12wCompoundPct = ToDecimal(s.Return12wCompoundPct),
        AnnVolatility12wPct  = ToDecimal(s.AnnVolatility12wPct),
        Sharpe12w            = ToDecimal(s.Sharpe12w),
        MaxDrawdown12wPct    = ToDecimal(s.MaxDrawdown12wPct),
        Return1yCompoundPct  = ToDecimal(s.Return1yCompoundPct),
        AnnVolatility1yPct   = ToDecimal(s.AnnVolatility1yPct),
        Sharpe1y             = ToDecimal(s.Sharpe1y),
        MaxDrawdown1yPct     = ToDecimal(s.MaxDrawdown1yPct),
    };

    private static decimal? ToDecimal(double value) => double.IsNaN(value) ? null : (decimal)value;
}
