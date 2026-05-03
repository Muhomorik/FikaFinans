namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class Position
{
    public required string Isin { get; init; }
    public string? Name { get; init; }
    public decimal CurrentValueKr { get; init; }
    public decimal CostBasisKr { get; init; }
}
