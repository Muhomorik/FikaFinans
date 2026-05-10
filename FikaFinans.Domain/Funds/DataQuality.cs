namespace FikaFinans.Domain.Funds;

public sealed class DataQuality
{
    public int MetadataRows { get; init; }
    public int SummaryRows { get; init; }
    public int SnapshotRows { get; init; }
    public int PositionsRows { get; init; }
    public int WriteoffCount { get; init; }
    public int CoreCount { get; init; }
    public List<string> Warnings { get; init; } = new();
}
