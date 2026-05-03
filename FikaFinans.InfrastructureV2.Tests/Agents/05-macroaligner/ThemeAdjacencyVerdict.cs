using FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

// Result of a single LLM adjacency classification. The agent translates this
// into MacroAlignment + MatchedTheme. We keep verdicts as their own type so
// stub clients in tests can return them without dragging in serialization
// concerns or having to construct full FundRecords.
public sealed class ThemeAdjacencyVerdict
{
    public required MacroAlignment Alignment { get; init; }
    public string? ThemeId { get; init; }
    public string? Rationale { get; init; }

    public static ThemeAdjacencyVerdict NoneVerdict { get; } = new()
    {
        Alignment = MacroAlignment.None,
        ThemeId   = null,
        Rationale = null,
    };
}
