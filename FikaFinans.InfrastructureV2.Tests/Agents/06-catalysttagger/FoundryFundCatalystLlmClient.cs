using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using OpenAI.Responses;

namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

// Foundry-backed catalyst classifier. Mirrors FoundryThemeAdjacencyLlmClient
// (step 05): create an ephemeral declarative agent with the system prompt baked
// in, fire one CreateResponseAsync per fund, then delete the agent. Failures
// (invalid JSON after one retry, non-Completed status) collapse to an empty
// classification list — the agent treats that as "no catalyst" and warns.
public sealed class FoundryFundCatalystLlmClient : IFundCatalystLlmClient
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private const string SystemPrompt = """
        You are a catalyst tagging agent. You receive a fund (name + category) and a
        list of active macro catalysts (each with affected_categories). Your job is
        to classify the fund's exposure to each catalyst as one of:

        - "Direct": the fund's category is in the catalyst's affected_categories
          list, or the fund's primary investment thesis is the catalyst.
        - "Indirect": the fund benefits secondarily through correlation, sector
          adjacency, or hedge-like behavior. NOT the primary beneficiary.
        - "None": no meaningful exposure.

        Return ONLY a JSON array (no prose, no markdown fences) with one entry per
        catalyst evaluated:
        [
          {"catalyst_name": "...", "exposure_type": "Direct"|"Indirect"|"None", "rationale": "..."}
        ]

        Constraints:
        - Do not invent catalysts. Only consider those provided in the input.
        - Direct exposure REQUIRES the fund category being in or very close to the
          catalyst's affected_categories. Cousin sectors are Indirect at best.
        - A fund can have at most one catalyst tagged in the final output. If
          multiple apply, the upstream agent will pick the strongest.
        - Each rationale must be ≤2 sentences and reference a specific mechanism.
        """;

    private const string RetryPromptPrefix = """
        Your previous response failed validation: {error}

        Re-emit valid JSON (array only, no prose, no fences). Use only the catalyst
        names from the input. Stay within the exposure_type enum {Direct, Indirect, None}.

        Original request follows:
        """;

    private readonly AIProjectClient _projectClient;
    private readonly string _modelId;

    public FoundryFundCatalystLlmClient(AIProjectClient projectClient, string modelId)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _projectClient = projectClient;
        _modelId = modelId;
    }

    public async Task<IReadOnlyList<CatalystExposureClassification>> ClassifyAsync(
        string fundName,
        string fundCategory,
        IReadOnlyList<Catalyst> activeCatalysts,
        CancellationToken ct = default)
    {
        if (activeCatalysts.Count == 0)
            return Array.Empty<CatalystExposureClassification>();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var validNames = activeCatalysts.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var userPrompt = BuildUserPrompt(fundName, fundCategory, activeCatalysts);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(agentName);

            var first = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            if (first.Value.Status == ResponseStatus.Completed)
            {
                if (TryParse(first.Value.GetOutputText(), validNames, out var parsed, out var error))
                    return parsed;

                // One corrective retry per the contract.
                var retryPrompt = RetryPromptPrefix.Replace("{error}", error ?? "invalid JSON")
                                  + "\n" + userPrompt;
                var second = await responseClient.CreateResponseAsync(retryPrompt, cancellationToken: timeoutCts.Token);
                if (second.Value.Status == ResponseStatus.Completed
                    && TryParse(second.Value.GetOutputText(), validNames, out var retried, out _))
                {
                    return retried;
                }
            }

            return Array.Empty<CatalystExposureClassification>();
        }
        catch
        {
            return Array.Empty<CatalystExposureClassification>();
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private static string BuildUserPrompt(
        string fundName,
        string fundCategory,
        IReadOnlyList<Catalyst> catalysts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Fund:");
        sb.Append("- name: ").AppendLine(fundName);
        sb.Append("- category: ").AppendLine(fundCategory);
        sb.AppendLine();
        sb.AppendLine("Active catalysts (name | intensity | weeks_active | affected_categories):");
        foreach (var c in catalysts)
        {
            sb.Append("- ").Append(c.Name)
              .Append(" | ").Append(c.Intensity)
              .Append(" | ").Append(c.WeeksActive)
              .Append(" | [").Append(string.Join(", ", c.AffectedCategories)).AppendLine("]");
        }
        sb.AppendLine();
        sb.AppendLine("Classify the fund's exposure to each catalyst. Return JSON array.");
        return sb.ToString();
    }

    private static bool TryParse(
        string raw,
        HashSet<string> validNames,
        out IReadOnlyList<CatalystExposureClassification> result,
        out string? error)
    {
        result = Array.Empty<CatalystExposureClassification>();
        error = null;
        try
        {
            var json = ExtractFirstJsonArray(raw);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "root is not a JSON array";
                return false;
            }

            var list = new List<CatalystExposureClassification>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.TryGetProperty("catalyst_name", out var n) ? n.GetString() : null;
                var typeRaw = element.TryGetProperty("exposure_type", out var et) ? et.GetString() : null;
                var rationale = element.TryGetProperty("rationale", out var r) ? r.GetString() : null;

                if (string.IsNullOrEmpty(name) || !validNames.Contains(name))
                    continue; // hallucinated name → drop

                if (!TryParseExposure(typeRaw, out var exposure))
                    continue;

                list.Add(new CatalystExposureClassification
                {
                    CatalystName = name,
                    Exposure     = exposure,
                    Rationale    = rationale,
                });
            }

            result = list;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseExposure(string? raw, out ExposureKind kind)
    {
        if (string.Equals(raw, "Direct", StringComparison.OrdinalIgnoreCase))
        {
            kind = ExposureKind.Direct;
            return true;
        }
        if (string.Equals(raw, "Indirect", StringComparison.OrdinalIgnoreCase))
        {
            kind = ExposureKind.Indirect;
            return true;
        }
        if (string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
        {
            kind = ExposureKind.None;
            return true;
        }
        kind = ExposureKind.None;
        return false;
    }

    private static string ExtractFirstJsonArray(string raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(raw);

        var start = raw.IndexOf('[');
        if (start < 0)
            throw new InvalidOperationException("response contains no '[' — cannot extract JSON array");

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return raw.Substring(start, i - start + 1);
            }
        }

        throw new InvalidOperationException("response has unbalanced brackets — cannot extract JSON array");
    }

    private async Task<(string Name, string Version)> CreateAgentAsync(CancellationToken ct)
    {
        var definition = new DeclarativeAgentDefinition(_modelId)
        {
            Instructions = SystemPrompt,
        };
        var safeModel = _modelId.Replace(".", "-").Replace("_", "-");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 16);
        var agentName = $"FikaFinans-Step6-{safeModel}-{shortId}";

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
