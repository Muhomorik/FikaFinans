namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

public sealed class MacroContext
{
    public required string GeneratedAt { get; init; }
    public required string IsoWeek { get; init; }
    public required string ConfigVersion { get; init; }
    public required SourceRunIds SourceRunIds { get; init; }
    public required MacroRegime MacroRegime { get; init; }
    public string? MacroRegimeSecondary { get; init; }
    public required decimal RegimeConfidence { get; init; }
    public required MarketSentiment NetMoodInput { get; init; }
    public required IReadOnlyList<Catalyst> Catalysts { get; init; }
    public required IReadOnlyList<RotationTheme> RotationThemes { get; init; }
    public IReadOnlyList<string>? Warnings { get; init; }
}
