namespace FikaFinans.Application.Agents;

/// <summary>
/// Provides the fund-analytics system prompt that ports the existing Claude.ai Project
/// instructions. Backed by an embedded resource shipped with the Infrastructure assembly.
/// </summary>
public interface IPromptProvider
{
    /// <summary>Returns the fund-analytics prompt — system instructions for every model run.</summary>
    AgentPrompt GetFundAnalyticsPrompt();
}
