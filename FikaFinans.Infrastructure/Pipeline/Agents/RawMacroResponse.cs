using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Infrastructure.Pipeline.Agents;

// What the LLM returns. The orchestrator merges this with run metadata
// (iso_week, source_run_ids, net_mood_input, theme ids) to build the final
// MacroContext.
internal sealed class RawMacroResponse
{
    public required MacroRegime MacroRegime { get; init; }
    public string? MacroRegimeSecondary { get; init; }
    public required decimal RegimeConfidence { get; init; }
    public required IReadOnlyList<RawCatalyst> Catalysts { get; init; }
    public required IReadOnlyList<RawRotationTheme> RotationThemes { get; init; }
}

internal sealed class RawCatalyst
{
    public required string Name { get; init; }
    public required Intensity Intensity { get; init; }
    public required int WeeksActive { get; init; }
    public required IReadOnlyList<string> AffectedCategories { get; init; }
    public required string Rationale { get; init; }
}

internal sealed class RawRotationTheme
{
    public required string Label { get; init; }
    public required SignalStrength SignalStrength { get; init; }
    public required IReadOnlyList<string> AffectedCategories { get; init; }
    public required string Rationale { get; init; }
    public SourceChain? SourceChain { get; init; }
}
