using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Macro;

/// <summary>
/// Step 3's store of computed macro contexts, one per set of weekly analytics reports.
/// Computed once, read by every fund of the week — and by steps 5 and 6.
/// </summary>
/// <remarks>
/// Keyed by the weekly summary's <c>runId</c>, the head of the reports' run id chain: a
/// corrected republish of a week has a new <c>runId</c>, so it is a miss and recomputes.
/// Last-write-wins, like every Tables-shaped store — two computes of the same reports
/// leave one row.
/// </remarks>
public interface IMacroContextStore
{
    /// <summary>
    /// Returns the context built from the reports headed by
    /// <paramref name="weeklySummaryRunId"/>, or <c>null</c> when none is stored.
    /// </summary>
    Task<MacroContext?> GetAsync(string weeklySummaryRunId, CancellationToken ct = default);

    /// <summary>
    /// Returns the newest context stored for <paramref name="notAfter"/> or any earlier
    /// week, or <c>null</c> when there is none. Newest means the latest week, then the
    /// latest <c>generatedAt</c> within it.
    /// </summary>
    Task<MacroContext?> GetNewestAsync(IsoWeek notAfter, CancellationToken ct = default);

    /// <summary>Stores <paramref name="context"/> under its weekly summary's <c>runId</c>.</summary>
    Task SaveAsync(MacroContext context, CancellationToken ct = default);
}
