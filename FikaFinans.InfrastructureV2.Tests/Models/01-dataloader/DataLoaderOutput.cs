namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class DataLoaderOutput
{
    public required string GeneratedAt { get; init; }
    public required string IsoWeek { get; init; }
    public required string Family { get; init; }
    public required string RunId { get; init; }
    public required string ConfigVersion { get; init; }
    public required IReadOnlyList<FundRecord> Funds { get; init; }
    public required IReadOnlyList<FrozenPosition> FrozenPositions { get; init; }
    public decimal CashAvailableKr { get; init; }
    public required DataQuality DataQuality { get; init; }
}
