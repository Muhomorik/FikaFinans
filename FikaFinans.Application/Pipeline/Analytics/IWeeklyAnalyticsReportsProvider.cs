using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Analytics;

/// <summary>
/// Seam over step 3's analytics input. The step knows it needs the
/// <see cref="WeeklyAnalyticsReports"/> for a week, not that they are three JSON files on
/// the desktop or a KanelBrief REST call in the cloud.
/// </summary>
/// <remarks>
/// Loads only — no validation beyond the week asked for. The agent keeps its checks
/// (status, matching weeks, <c>runId</c> chain), so reports that fail them are still
/// returned here.
/// </remarks>
public interface IWeeklyAnalyticsReportsProvider
{
    /// <summary>
    /// Returns the latest reports for <paramref name="notAfter"/> or an earlier week, read
    /// fresh on every call — or <c>null</c> when there are none. Reports for a later week are
    /// never returned: a fund is not analysed against a week it has not reached.
    /// </summary>
    /// <param name="notAfter">
    /// The fund's week. Matched against the weekly summary's <c>periodIsoWeek</c>; the
    /// caller reads the week actually returned from it.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    /// <exception cref="InvalidDataException">A report is present but cannot be read.</exception>
    Task<WeeklyAnalyticsReports?> GetLatestAsync(IsoWeek notAfter, CancellationToken ct = default);
}
