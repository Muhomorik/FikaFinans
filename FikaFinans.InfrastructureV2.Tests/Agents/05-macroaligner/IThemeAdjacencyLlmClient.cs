using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

public interface IThemeAdjacencyLlmClient
{
    Task<ThemeAdjacencyVerdict> ClassifyAsync(
        string fundCategory,
        IReadOnlyList<RotationTheme> activeThemes,
        CancellationToken ct = default);
}
