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
    /// Returns the reports for <paramref name="isoWeek"/>, read fresh on every call, or
    /// <c>null</c> when there are none for that week.
    /// </summary>
    /// <param name="isoWeek">Matched against the weekly summary's <c>periodIsoWeek</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <exception cref="InvalidDataException">A report is present but cannot be read.</exception>
    Task<WeeklyAnalyticsReports?> GetAsync(IsoWeek isoWeek, CancellationToken ct = default);
}
