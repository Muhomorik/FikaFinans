using FikaFinans.Domain.KanelBulleKapital;

namespace FikaFinans.Domain.Macro;

public sealed class SubstitutionChainRun
{
    public required string ReportType { get; init; }
    public required SubstitutionChainRunId RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required string PeriodIsoWeek { get; init; }
    public required WeeklySummaryRunId WeeklySummaryRunId { get; init; }
    public required IReadOnlyList<RotationChain> Chains { get; init; }
}

public sealed class RotationChain
{
    public required string CapitalFleeing { get; init; }
    public required string FlowsToward { get; init; }
    public required string Mechanism { get; init; }
}
