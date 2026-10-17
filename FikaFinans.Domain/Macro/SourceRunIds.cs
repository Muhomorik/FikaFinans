using FikaFinans.Domain.KanelBulleKapital;

namespace FikaFinans.Domain.Macro;

public sealed class SourceRunIds
{
    public required WeeklySummaryRunId WeeklySummaryRunId { get; init; }
    public required SubstitutionChainRunId SubstitutionChainRunId { get; init; }
    public required OpportunityScanRunId RotationTargetsRunId { get; init; }
}
