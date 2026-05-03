namespace FikaFinans.Application.Agents;

/// <summary>
/// Value object representing a loaded agent prompt — display name plus the system-prompt body
/// sent to the LLM as <c>instructions</c>.
/// </summary>
public sealed record AgentPrompt(string Name, string SystemPrompt);
