using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Analytics;

/// <summary>
/// The three KanelBrief weekly analytics reports step 3 reads together, as the typed runs
/// the agent takes. Each property is named after its report's <c>reportType</c>.
/// </summary>
/// <remarks>
/// Carried as read, not checked: whether the three agree on <c>periodIsoWeek</c> and the
/// <c>runId</c> chain is the agent's validation, not the loader's.
/// </remarks>
public sealed record WeeklyAnalyticsReports(
    WeeklySummaryRun WeeklySummary,
    SubstitutionChainRun SubstitutionChain,
    OpportunityScanRun RotationTargets);
