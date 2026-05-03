namespace FikaFinans.Application.Agents;

/// <summary>
/// Provides the default user prompt — the question text shown in the Compare Models entry box
/// when the user hasn't customized one yet. Distinct from <see cref="IPromptProvider"/>, which
/// supplies the system instructions baked into every agent run.
/// </summary>
/// <remarks>
/// Today this is a hardcoded baseline. The roadmap is a header-menu editor that persists a
/// custom prompt to <c>%APPDATA%\FikaFinans\</c> with a "reset to default" path back to
/// whatever this provider returns.
/// </remarks>
public interface IDefaultUserPromptProvider
{
    /// <summary>Returns the canonical default user prompt — never null or empty.</summary>
    string GetDefaultUserPrompt();
}
