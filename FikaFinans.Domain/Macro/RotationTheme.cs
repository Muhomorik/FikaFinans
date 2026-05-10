namespace FikaFinans.Domain.Macro;

public sealed class RotationTheme
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required SignalStrength SignalStrength { get; init; }
    public required IReadOnlyList<string> AffectedCategories { get; init; }
    public required string Rationale { get; init; }
    public SourceChain? SourceChain { get; init; }
}
