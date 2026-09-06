namespace FikaFinans.Application.YieldRaccoon;

/// <summary>
/// Produces a per-fund rolling-horizon snapshot CSV — one row per fund, anchored at the most recent
/// NAV date in the database. Carries 12-week and 1-year aggregates so cloud agents can read current-state
/// metrics without daily-NAV access. Companion to <see cref="IFundStatisticsCsvExportService"/> (history)
/// and <c>IFundMetadataCsvExportService</c> (identity).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrored from the YieldRaccoon producer</b> (<c>YieldRaccoon.Application.Services</c>).
/// This is a copy of an upstream dependency's contract, not a FikaFinans-owned abstraction —
/// keep it in sync with the original so the two can be diffed when YR changes. The reference to
/// the metadata export service is a <c>&lt;c&gt;</c> tag rather than a <c>&lt;see cref&gt;</c>
/// because that third service has no mirror on this side.
/// </para>
/// <para>Reads the source database in read-only mode — nothing is modified.</para>
/// <para>Output schema (10 columns):</para>
/// <list type="bullet">
///   <item><c>isin</c>, <c>as_of_date</c></item>
///   <item><c>return_12w_compound_pct</c>, <c>ann_volatility_12w_pct</c>, <c>sharpe_12w</c>, <c>max_drawdown_12w_pct</c></item>
///   <item><c>return_1y_compound_pct</c>, <c>ann_volatility_1y_pct</c>, <c>sharpe_1y</c>, <c>max_drawdown_1y_pct</c></item>
/// </list>
/// <para>
/// All four <c>_12w_*</c> columns are <c>NaN</c> when the fund has fewer than ~84 days of history; all four
/// <c>_1y_*</c> columns are <c>NaN</c> when the fund has fewer than ~365 days of history. Sharpe is also
/// <c>NaN</c> when the horizon volatility falls below 0.01 % (essentially constant NAV — guards against
/// bond-fund Sharpe explosions).
/// </para>
/// <para>
/// The emitted file is the producer counterpart of the snapshot CSV consumed by
/// <c>SnapshotCsvParser</c>.
/// </para>
/// </remarks>
public interface IFundSnapshotCsvExportService
{
    /// <summary>
    /// Reads fund NAV data, computes 12-week and 1-year rolling-horizon metrics anchored at the latest
    /// NAV date in the database, and writes results to CSV.
    /// </summary>
    /// <param name="sourceDatabasePath">Path to the SQLite database file containing fund data.</param>
    /// <param name="csvOutputPath">Path where the CSV file will be written.</param>
    /// <param name="companyName">Optional company name filter (case-insensitive). Null or empty includes all companies.</param>
    /// <param name="minNumberOfOwners">Minimum number of owners a fund must have to be included (0 to skip filter).</param>
    /// <param name="progress">Optional progress reporter. Reports (processed fund count, total fund count).</param>
    /// <returns>The total number of rows written to the CSV file (header excluded).</returns>
    Task<int> ExportAsync(
        string sourceDatabasePath,
        string csvOutputPath,
        string? companyName = null,
        int minNumberOfOwners = 0,
        IProgress<(int processed, int total)>? progress = null);
}
