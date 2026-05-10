namespace FikaFinans.Application.Pipeline.Llm;

public interface IMacroLlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
