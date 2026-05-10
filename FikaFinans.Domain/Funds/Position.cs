using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Funds;

public sealed class Position
{
    public required Isin Isin { get; init; }
    public string? Name { get; init; }
    public decimal CurrentValueKr { get; init; }
    public decimal CostBasisKr { get; init; }
}
