namespace FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;

public sealed class MetricsDataQuality
{
    public int BucketsUsed { get; init; }
    public bool SnapshotMissing { get; init; }
    public bool SnapshotStaleVsSummary { get; init; }
    public bool Sharpe2wIsNan { get; init; }
    public bool Sharpe12wIsNan { get; init; }
    public bool Sharpe1yIsNan { get; init; }
}
