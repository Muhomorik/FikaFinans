namespace FikaFinans.Domain.Macro;

public sealed class Catalyst
{
    public required string Name { get; init; }
    public required Intensity Intensity { get; init; }
    public required int WeeksActive { get; init; }
    public required IReadOnlyList<string> AffectedCategories { get; init; }
    public required string Rationale { get; init; }
}
