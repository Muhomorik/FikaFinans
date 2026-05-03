using System.Reflection;
using FikaFinans.Application.Agents;

namespace FikaFinans.Infrastructure.Prompts;

/// <summary>
/// Loads the fund-analytics prompt from an embedded resource shipped with this assembly.
/// Source file: <c>FikaFinans/Docs/prompt_analytics.md</c>, embedded with
/// <c>Link="Prompts\fund_analytics.prompt.md"</c>.
/// </summary>
public sealed class EmbeddedPromptProvider : IPromptProvider
{
    private const string ResourceName = "FikaFinans.Infrastructure.Prompts.fund_analytics.prompt.md";
    private const string FallbackName = "FundAnalytics";

    private readonly Lazy<AgentPrompt> _prompt;

    public EmbeddedPromptProvider()
    {
        _prompt = new Lazy<AgentPrompt>(LoadPrompt, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public AgentPrompt GetFundAnalyticsPrompt() => _prompt.Value;

    private static AgentPrompt LoadPrompt()
    {
        var assembly = typeof(EmbeddedPromptProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            var available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded prompt resource '{ResourceName}' not found in {assembly.GetName().Name}. " +
                $"Available: [{available}]. Check that <EmbeddedResource Include=\"..\\Docs\\prompt_analytics.md\" " +
                $"Link=\"Prompts\\fund_analytics.prompt.md\" /> is present in FikaFinans.Infrastructure.csproj.");
        }

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var (name, body) = PromptFileParser.Parse(content, FallbackName);
        return new AgentPrompt(name, body);
    }
}
