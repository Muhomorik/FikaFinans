namespace FikaFinans.Application.YieldRaccoon;

/// <summary>
/// Service for computing fund summary statistics from a YieldRaccoon SQLite database
/// and exporting them as a CSV file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrored from the YieldRaccoon producer</b> (<c>YieldRaccoon.Application.Services</c>).
/// This is a copy of an upstream dependency's contract, not a FikaFinans-owned abstraction —
/// keep it in sync with the original so the two can be diffed when YR changes.
/// </para>
/// <para>
/// <b>Ahead of upstream:</b> <see cref="ComputeAsync"/> does not exist in YR yet. It was added
/// here first and has to be ported back by hand; until that happens this contract is a superset
/// of the original, so diff on <see cref="ExportAsync"/> alone.
/// </para>
/// <para>Reads the source database in read-only mode — nothing is modified or deleted.</para>
/// <para>
/// Slices each fund's NAV history into non-overlapping time windows (e.g., 2 weeks),
/// computing 13 summary statistics per window:
/// </para>
/// <list type="bullet">
///   <item>first_nav, last_nav, nav_high, nav_low</item>
///   <item>total_return_pct, ann_volatility, max_drawdown_pct, current_drawdown_pct</item>
///   <item>sharpe_ratio, best_day_pct, worst_day_pct, pct_positive_days, skewness</item>
/// </list>
/// <para>
/// The emitted file is the producer counterpart of the summary CSV consumed by
/// <c>SummaryCsvParser</c>.
/// </para>
/// </remarks>
public interface IFundStatisticsCsvExportService
{
    /// <summary>
    /// Reads fund NAV data and computes summary statistics per time window, returning them in
    /// memory. The disk-free counterpart of <see cref="ExportAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ExportAsync"/> is this method plus a CSV serialization step, so both paths see
    /// identical windowing and identical arithmetic. They are not bit-identical in their results,
    /// though: the CSV writer formats every metric with <c>F4</c>, so the file rounds to four
    /// decimal places while this method hands back the full <see cref="double"/> precision. A
    /// value computed here will not always re-parse from the exported CSV as the same number.
    /// </para>
    /// <para>
    /// The CSV path also rejects a duplicate (<c>Isin</c>, <c>PeriodStart</c>) pair as a
    /// corrupt-output guard. This method applies no such check — it reports what the windowing
    /// produced.
    /// </para>
    /// </remarks>
    /// <param name="sourceDatabasePath">Path to the SQLite database file containing fund data.</param>
    /// <param name="windowSizeDays">Size of each non-overlapping time window in calendar days (e.g., 14 for 2 weeks).</param>
    /// <param name="companyName">Optional company name filter (case-insensitive). Null or empty to include all companies.</param>
    /// <param name="minNumberOfOwners">Minimum number of owners a fund must have to be included (0 to skip filter).</param>
    /// <param name="cutoffDate">Optional earliest date for NAV data. Data before this date is excluded. Null to include all history.</param>
    /// <param name="progress">Optional progress reporter. Reports (processed fund count, total fund count).</param>
    /// <returns>
    /// One entry per fund per emitted window, ordered by ISIN then chronologically within a fund.
    /// Empty when no fund passes the filters.
    /// </returns>
    Task<IReadOnlyList<FundSummaryStatistics>> ComputeAsync(
        string sourceDatabasePath,
        int windowSizeDays,
        string? companyName = null,
        int minNumberOfOwners = 0,
        DateOnly? cutoffDate = null,
        IProgress<(int processed, int total)>? progress = null);

    /// <summary>
    /// Reads fund NAV data, computes summary statistics per time window, and writes results to CSV.
    /// </summary>
    /// <param name="sourceDatabasePath">Path to the SQLite database file containing fund data.</param>
    /// <param name="csvOutputPath">Path where the CSV file will be written.</param>
    /// <param name="windowSizeDays">Size of each non-overlapping time window in calendar days (e.g., 14 for 2 weeks).</param>
    /// <param name="companyName">Optional company name filter (case-insensitive). Null or empty to include all companies.</param>
    /// <param name="minNumberOfOwners">Minimum number of owners a fund must have to be included (0 to skip filter).</param>
    /// <param name="cutoffDate">Optional earliest date for NAV data. Data before this date is excluded. Null to include all history.</param>
    /// <param name="progress">Optional progress reporter. Reports (processed fund count, total fund count).</param>
    /// <returns>The total number of rows written to the CSV file.</returns>
    Task<int> ExportAsync(
        string sourceDatabasePath,
        string csvOutputPath,
        int windowSizeDays,
        string? companyName = null,
        int minNumberOfOwners = 0,
        DateOnly? cutoffDate = null,
        IProgress<(int processed, int total)>? progress = null);
}
