using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Models;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using FikaFinans.Infrastructure.Pipeline.Json;
using OpenAI.Responses;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Foundry-backed adjacency classifier. Mirrors FoundryMacroLlmClient (step 03):
// create an ephemeral declarative agent with the system prompt baked in,
// fire one CreateResponseAsync, then delete the agent. Failures (invalid JSON,
// non-Completed status, unknown theme_id) collapse to NoneVerdict, as the contract
// says — so each one is logged here at Warn: to the caller it looks like a real None.
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

    private readonly ILogger _logger;
    private readonly AIProjectClient _projectClient;
    private readonly FoundryDeploymentName _deploymentName;

    /// <param name="logger">Where the call, the verdict and why a failure became None are logged.</param>
    /// <param name="projectClient">The Foundry project the ephemeral agent is created in.</param>
    /// <param name="deploymentName">
    /// The Foundry deployment the agent runs on — the name the portal gave the deployment, not
    /// the model family: e.g. <c>gpt-5.4-1</c> or <c>DeepSeek-R1-0528-1</c>, see
    /// <see cref="Infrastructure.Foundry.FoundryModelIds"/>.
    /// </param>
    public FoundryThemeAdjacencyLlmClient(ILogger logger, AIProjectClient projectClient, FoundryDeploymentName deploymentName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName.Value, nameof(deploymentName));
        _logger = logger;
        _projectClient = projectClient;
        _deploymentName = deploymentName;
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

        _logger.Debug("Adjacency LLM call — model {0}, category '{1}', {2} theme(s), timeout {3}",
            _deploymentName, fundCategory, activeThemes.Count, RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        _logger.Trace("Adjacency LLM agent created — {0} v{1}", agentName, agentVersion);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);
            var responseWrapper = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            var response = responseWrapper.Value;
            if (response.Status != ResponseStatus.Completed)
            {
                _logger.Warn("Adjacency LLM response not completed — {0}, status {1}: defaulting to None",
                    agentName, response.Status);
                return ThemeAdjacencyVerdict.NoneVerdict;
            }

            var text = response.GetOutputText();
            _logger.Trace("Adjacency LLM response — {0}: {1}", agentName, text);

            var verdict = ParseVerdict(text, activeThemes, agentName);
            _logger.Debug("Adjacency LLM verdict — category '{0}': {1}, theme {2}, rationale: {3}",
                fundCategory, verdict.Alignment, verdict.ThemeId ?? "-", verdict.Rationale ?? "-");
            return verdict;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Warn("Adjacency LLM call timed out — {0}, after {1}: defaulting to None", agentName, RunTimeout);
            return ThemeAdjacencyVerdict.NoneVerdict;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn(ex, "Adjacency LLM call failed — {0}: defaulting to None", agentName);
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

    private ThemeAdjacencyVerdict ParseVerdict(string raw, IReadOnlyList<RotationTheme> themes, string agentName)
    {
        try
        {
            var json = JsonExtraction.ExtractFirstJsonObject(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var rationale = root.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;

            var alignmentRaw = root.TryGetProperty("alignment", out var a) ? a.GetString() : null;
            if (!string.Equals(alignmentRaw, "Partial", StringComparison.OrdinalIgnoreCase))
                return new ThemeAdjacencyVerdict { Alignment = MacroAlignment.None, Rationale = rationale };

            var themeId = root.TryGetProperty("theme_id", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            if (string.IsNullOrEmpty(themeId) || themes.All(x => x.Id != themeId))
            {
                _logger.Warn("Adjacency LLM answered Partial with unknown theme '{0}' — {1}: defaulting to None",
                    themeId, agentName);
                return ThemeAdjacencyVerdict.NoneVerdict;
            }

            return new ThemeAdjacencyVerdict
            {
                Alignment = MacroAlignment.Partial,
                ThemeId   = themeId,
                Rationale = rationale,
            };
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Adjacency LLM answer not parsed — {0}: defaulting to None. Raw: {1}", agentName, raw);
            return ThemeAdjacencyVerdict.NoneVerdict;
        }
    }

    private async Task<(string Name, string Version)> CreateAgentAsync(CancellationToken ct)
    {
        var definition = new DeclarativeAgentDefinition(_deploymentName.Value)
        {
            Instructions = SystemPrompt,
        };
        var safeModel = _deploymentName.Value.Replace(".", "-").Replace("_", "-");
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
            _logger.Trace("Adjacency LLM agent deleted — {0} v{1}", agentName, agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
        catch (ClientResultException ex) when (ex.Status == 404) { }
        catch (Exception ex)
        {
            // Best-effort cleanup; stragglers can be swept later.
            _logger.Warn(ex, "Adjacency LLM agent not deleted — {0} v{1}, left for a sweep", agentName, agentVersion);
        }
    }
}
