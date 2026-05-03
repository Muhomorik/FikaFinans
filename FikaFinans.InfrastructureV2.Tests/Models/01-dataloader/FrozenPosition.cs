namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class FrozenPosition
{
    public required string Name { get; init; }
    public string? Isin { get; init; }
    public decimal CurrentValueKr { get; init; }
    public decimal? CostBasisKr { get; init; }
    public required string Reason { get; init; }
}
