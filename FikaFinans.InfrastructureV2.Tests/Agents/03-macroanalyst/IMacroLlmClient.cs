namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

public interface IMacroLlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
