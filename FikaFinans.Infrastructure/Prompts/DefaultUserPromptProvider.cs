using FikaFinans.Application.Agents;

namespace FikaFinans.Infrastructure.Prompts;

/// <summary>
/// Returns a hardcoded weekly-review question as the default user prompt. The next iteration
/// will read a user-edited copy from <c>%LOCALAPPDATA%\FikaFinans\</c> and fall back to this
/// constant when the file is absent or the user clicks "reset to default".
/// </summary>
public sealed class DefaultUserPromptProvider : IDefaultUserPromptProvider
{
    private const string DefaultPrompt =
        "Run my weekly portfolio analysis: list any exit-layer signals due this week, " +
        "satellite funds matching the sell-signal criteria, and any funds matching the " +
        "buy-signal criteria from the wider fund universe. For each, show the rule that " +
        "triggered and the supporting numbers.";

    public string GetDefaultUserPrompt() => DefaultPrompt;
}
