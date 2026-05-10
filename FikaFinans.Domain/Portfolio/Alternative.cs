using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Portfolio;

public sealed class Alternative
{
    public required Isin Isin { get; init; }
    public required string Name { get; init; }
    public required string Differentiator { get; init; }
}
