using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using OpenAI.Responses;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

// Foundry-backed adjacency classifier. Mirrors FoundryMacroLlmClient (step 03):
// create an ephemeral declarative agent with the system prompt baked in,
// fire one CreateResponseAsync, then delete the agent. Failures (invalid JSON,
// non-Completed status, unknown theme_id) collapse to NoneVerdict — the agent
// turns those into warnings.
public sealed class FoundryThemeAdjacencyLlmClient : IThemeAdjacencyLlmClient
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private const string SystemPrompt = """
        You are evaluating whether a fund's category has "partial" alignment with any
        active macro rotation theme. Partial alignment means defensive proximity, sector
        adjacency, or factor overlap — not direct category match.

        Return one of:
        - {"alignment": "Partial", "theme_id": "<id>", "rationale": "<one sentence>"}
        - {"alignment": "None", "theme_id": null, "rationale": "<one sentence>"}

        Be conservative. Default to None unless there is a clear adjacency reason.
        Return ONLY the JSON object, no prose, no markdown fences.
        """;

    private readonly AIProjectClient _projectClient;
    private readonly string _modelId;

    public FoundryThemeAdjacencyLlmClient(AIProjectClient projectClient, string modelId)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _projectClient = projectClient;
        _modelId = modelId;
    }

    public async Task<ThemeAdjacencyVerdict> ClassifyAsync(
        string fundCategory,
        IReadOnlyList<RotationTheme> activeThemes,
        CancellationToken ct = default)
    {
        if (activeThemes.Count == 0)
            return ThemeAdjacencyVerdict.NoneVerdict;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var userPrompt = BuildUserPrompt(fundCategory, activeThemes);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);
            var responseWrapper = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            var response = responseWrapper.Value;
            if (response.Status != ResponseStatus.Completed)
                return ThemeAdjacencyVerdict.NoneVerdict;

            return ParseVerdict(response.GetOutputText(), activeThemes);
        }
        catch
        {
            return ThemeAdjacencyVerdict.NoneVerdict;
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private static string BuildUserPrompt(string fundCategory, IReadOnlyList<RotationTheme> themes)
    {
        var sb = new StringBuilder();
        sb.Append("Fund category: ").AppendLine(fundCategory);
        sb.AppendLine();
        sb.AppendLine("Active themes (id, label, affected_categories):");
        foreach (var t in themes)
        {
            sb.Append("- ").Append(t.Id).Append(" | ").Append(t.Label).Append(" | [")
              .Append(string.Join(", ", t.AffectedCategories)).AppendLine("]");
        }
        sb.AppendLine();
        sb.AppendLine("Is there partial alignment? Return JSON.");
        return sb.ToString();
    }

    private static ThemeAdjacencyVerdict ParseVerdict(string raw, IReadOnlyList<RotationTheme> themes)
    {
        try
        {
            var json = JsonExtraction.ExtractFirstJsonObject(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var alignmentRaw = root.TryGetProperty("alignment", out var a) ? a.GetString() : null;
            if (!string.Equals(alignmentRaw, "Partial", StringComparison.OrdinalIgnoreCase))
                return ThemeAdjacencyVerdict.NoneVerdict;

            var themeId = root.TryGetProperty("theme_id", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            if (string.IsNullOrEmpty(themeId) || themes.All(x => x.Id != themeId))
                return ThemeAdjacencyVerdict.NoneVerdict;

            var rationale = root.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;

            return new ThemeAdjacencyVerdict
            {
                Alignment = MacroAlignment.Partial,
                ThemeId   = themeId,
                Rationale = rationale,
            };
        }
        catch
        {
            return ThemeAdjacencyVerdict.NoneVerdict;
        }
    }

    private async Task<(string Name, string Version)> CreateAgentAsync(CancellationToken ct)
    {
        var definition = new DeclarativeAgentDefinition(_modelId)
        {
            Instructions = SystemPrompt,
        };
        var safeModel = _modelId.Replace(".", "-").Replace("_", "-");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 16);
        var agentName = $"FikaFinans-Step5-{safeModel}-{shortId}";

        var response = await _projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
            agentName,
            new ProjectsAgentVersionCreationOptions(definition),
            cancellationToken: ct);
        return (response.Value.Name, response.Value.Version);
    }

    private async Task TryDeleteAgentAsync(string agentName, string agentVersion)
    {
        try
        {
            await _projectClient.AgentAdministrationClient.DeleteAgentVersionAsync(agentName, agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
        catch (ClientResultException ex) when (ex.Status == 404) { }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
