using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Llm;

public interface IThemeAdjacencyLlmClient
{
    Task<ThemeAdjacencyVerdict> ClassifyAsync(
        string fundCategory,
        IReadOnlyList<RotationTheme> activeThemes,
        CancellationToken ct = default);
}
