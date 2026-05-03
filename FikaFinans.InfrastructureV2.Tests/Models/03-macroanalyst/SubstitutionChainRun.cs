namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

public sealed class SubstitutionChainRun
{
    public required string ReportType { get; init; }
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required string PeriodIsoWeek { get; init; }
    public required string WeeklySummaryRunId { get; init; }
    public required IReadOnlyList<RotationChain> Chains { get; init; }
}

public sealed class RotationChain
{
    public required string CapitalFleeing { get; init; }
    public required string FlowsToward { get; init; }
    public required string Mechanism { get; init; }
}
