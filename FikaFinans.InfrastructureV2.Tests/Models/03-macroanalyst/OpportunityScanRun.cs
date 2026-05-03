namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

public sealed class OpportunityScanRun
{
    public required string ReportType { get; init; }
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required string PeriodIsoWeek { get; init; }
    public required string SubstitutionChainRunId { get; init; }
    public required IReadOnlyList<RotationTarget> Targets { get; init; }
}

public sealed class RotationTarget
{
    public required string Category { get; init; }
    public required SignalStrength SignalStrength { get; init; }
    public required string Rationale { get; init; }
    public required string RiskCaveat { get; init; }
}
