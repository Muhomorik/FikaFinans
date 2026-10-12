using System.Text.Json;

using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Analytics;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Analytics;

/// <summary>
/// <see cref="IWeeklyAnalyticsReportsProvider"/> backed by the three <c>analytics-*.json</c>
/// files, located through <see cref="IPathsService"/> — the same files the agent's
/// <c>RunAsync</c> reads.
/// </summary>
/// <remarks>
/// Fixed names, latest copy only: the files hold whichever week was exported last, and that
/// is what is returned — unless it is later than the week asked for. Read on every call
/// rather than cached, so a new export applies to the next fund without a restart.
/// </remarks>
public sealed class FileBackedWeeklyAnalyticsReportsProvider : IWeeklyAnalyticsReportsProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public FileBackedWeeklyAnalyticsReportsProvider(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public async Task<WeeklyAnalyticsReports?> GetLatestAsync(IsoWeek notAfter, CancellationToken ct = default)
    {
        var weeklySummaryPath     = _paths.AnalyticsWeeklySummaryJson;
        var substitutionChainPath = _paths.AnalyticsSubstitutionChainJson;
        var rotationTargetsPath   = _paths.AnalyticsRotationTargetsJson;

        // A partial set counts as none: the three are only meaningful together, and the
        // caller treats "none" as "nothing to compute with" rather than an error.
        var missing = new[] { weeklySummaryPath, substitutionChainPath, rotationTargetsPath }
            .Where(p => !File.Exists(p))
            .ToList();
        if (missing.Count > 0)
        {
            _logger.Warn(
                "Weekly analytics reports not available — expected week {0} or earlier, missing: {1}",
                notAfter, string.Join(", ", missing));
            return null;
        }

        var weeklySummary = await ReadAsync<WeeklySummaryRun>(weeklySummaryPath, ct).ConfigureAwait(false);
        var found = weeklySummary.PeriodIsoWeek;

        // Only the weekly summary's week is matched: the other two disagreeing with it is the
        // agent's validation to report, not a reason to pretend there are no reports. ISO week
        // labels are zero-padded, so ordinal order is week order.
        if (string.CompareOrdinal(found, notAfter.Value) > 0)
        {
            _logger.Info(
                "Weekly analytics reports not used — expected week {0} or earlier, {1} holds {2}",
                notAfter, weeklySummaryPath, found);
            return null;
        }

        var substitutionChain = await ReadAsync<SubstitutionChainRun>(substitutionChainPath, ct).ConfigureAwait(false);
        var rotationTargets   = await ReadAsync<OpportunityScanRun>(rotationTargetsPath, ct).ConfigureAwait(false);

        // What was expected next to what is used, with the files, so a stale export is
        // visible in the log rather than only in the context's week.
        var message =
            "Weekly analytics reports used — expected week {0}, using {1} (weekly summary run {2}) from {3}, {4}, {5}";
        if (found == notAfter.Value)
            _logger.Debug(message, notAfter, found, weeklySummary.RunId, weeklySummaryPath, substitutionChainPath, rotationTargetsPath);
        else
            _logger.Info(message, notAfter, found, weeklySummary.RunId, weeklySummaryPath, substitutionChainPath, rotationTargetsPath);

        return new WeeklyAnalyticsReports(weeklySummary, substitutionChain, rotationTargets);
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct) where T : class
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        T? report;
        try
        {
            report = JsonSerializer.Deserialize<T>(json, AnalyticsJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Analytics report is not valid JSON — {path}", ex);
        }

        return report ?? throw new InvalidDataException($"Analytics report is empty — {path}");
    }
}
