using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Funds;

public sealed class PinnedFund
{
    public Isin? Isin { get; init; }
    public string? Name { get; init; }
    public required PinnedLayer Layer { get; init; }
}
