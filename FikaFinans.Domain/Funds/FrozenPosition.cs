using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Funds;

public sealed class FrozenPosition
{
    public required string Name { get; init; }
    public Isin? Isin { get; init; }
    public decimal CurrentValueKr { get; init; }
    public decimal? CostBasisKr { get; init; }
    public required string Reason { get; init; }
}
