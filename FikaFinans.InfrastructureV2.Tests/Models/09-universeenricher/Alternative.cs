namespace FikaFinans.InfrastructureV2.Tests.Models.UniverseEnricher;

public sealed class Alternative
{
    public required string Isin { get; init; }
    public required string Name { get; init; }
    public required string Differentiator { get; init; }
}
