using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Llm;

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
