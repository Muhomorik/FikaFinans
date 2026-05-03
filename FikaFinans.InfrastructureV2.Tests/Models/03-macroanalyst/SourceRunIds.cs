namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

public sealed class SourceRunIds
{
    public required string WeeklySummaryRunId { get; init; }
    public required string SubstitutionChainRunId { get; init; }
    public required string RotationTargetsRunId { get; init; }
}
