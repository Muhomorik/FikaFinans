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
/// Fixed names, latest copy only: the files hold whichever week was exported last, so any
/// other week asked for is "none". Read on every call rather than cached, so a new export
/// applies to the next fund without a restart.
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
    public async Task<WeeklyAnalyticsReports?> GetAsync(IsoWeek isoWeek, CancellationToken ct = default)
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
            _logger.Warn("Weekly analytics reports not available, missing — {0}", string.Join(", ", missing));
            return null;
        }

        var weeklySummary = await ReadAsync<WeeklySummaryRun>(weeklySummaryPath, ct).ConfigureAwait(false);

        // Only the weekly summary's week is matched: the other two disagreeing with it is the
        // agent's validation to report, not a reason to pretend the week has no reports.
        if (!string.Equals(weeklySummary.PeriodIsoWeek, isoWeek.Value, StringComparison.Ordinal))
        {
            _logger.Info("Weekly analytics reports not available for {0} — the files hold {1}",
                isoWeek, weeklySummary.PeriodIsoWeek);
            return null;
        }

        var substitutionChain = await ReadAsync<SubstitutionChainRun>(substitutionChainPath, ct).ConfigureAwait(false);
        var rotationTargets   = await ReadAsync<OpportunityScanRun>(rotationTargetsPath, ct).ConfigureAwait(false);

        _logger.Trace("Weekly analytics reports read — week {0}, weekly summary run {1}",
            weeklySummary.PeriodIsoWeek, weeklySummary.RunId);

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
