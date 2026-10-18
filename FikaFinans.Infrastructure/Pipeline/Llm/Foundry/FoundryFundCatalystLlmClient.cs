using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Models;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Domain.Macro;
using OpenAI.Responses;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Foundry-backed catalyst classifier. Mirrors FoundryThemeAdjacencyLlmClient
// (step 05): create an ephemeral declarative agent with the system prompt baked
// in, fire one CreateResponseAsync per fund, then delete the agent. Failures
// (invalid JSON after one retry, non-Completed status, timeout) collapse to an empty
// classification list, as the contract says — so each one is logged here at Warn: to
// the caller it looks like a real "no exposure".
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

    private readonly ILogger _logger;
    private readonly AIProjectClient _projectClient;
    private readonly FoundryDeploymentName _deploymentName;

    /// <param name="logger">Where the call, the verdicts and why a failure became "no exposure" are logged.</param>
    /// <param name="projectClient">The Foundry project the ephemeral agent is created in.</param>
    /// <param name="deploymentName">
    /// The Foundry deployment the agent runs on — the name the portal gave the deployment, not
    /// the model family: e.g. <c>gpt-5.4-1</c> or <c>DeepSeek-R1-0528-1</c>, see
    /// <see cref="Infrastructure.Foundry.FoundryModelIds"/>.
    /// </param>
    public FoundryFundCatalystLlmClient(ILogger logger, AIProjectClient projectClient, FoundryDeploymentName deploymentName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName.Value, nameof(deploymentName));
        _logger = logger;
        _projectClient = projectClient;
        _deploymentName = deploymentName;
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

        _logger.Debug("Catalyst LLM call — model {0}, fund '{1}', category '{2}', {3} catalyst(s), timeout {4}",
            _deploymentName, fundName, fundCategory, activeCatalysts.Count, RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        _logger.Trace("Catalyst LLM agent created — {0} v{1}", agentName, agentVersion);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(agentName);

            var first = (await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token)).Value;
            if (first.Status != ResponseStatus.Completed)
            {
                _logger.Warn("Catalyst LLM response not completed — {0}, status {1}: defaulting to no catalyst",
                    agentName, first.Status);
                return Array.Empty<CatalystExposureClassification>();
            }

            var firstText = first.GetOutputText();
            _logger.Trace("Catalyst LLM response — {0}: {1}", agentName, firstText);

            if (TryParse(firstText, validNames, agentName, out var parsed, out var error))
                return LogVerdicts(fundName, parsed);

            // One corrective retry per the contract.
            _logger.Warn("Catalyst LLM answer not parsed — {0}: {1}; retrying once. Raw: {2}",
                agentName, error, firstText);

            var retryPrompt = RetryPromptPrefix.Replace("{error}", error ?? "invalid JSON")
                              + "\n" + userPrompt;
            var second = (await responseClient.CreateResponseAsync(retryPrompt, cancellationToken: timeoutCts.Token)).Value;
            if (second.Status != ResponseStatus.Completed)
            {
                _logger.Warn("Catalyst LLM retry not completed — {0}, status {1}: defaulting to no catalyst",
                    agentName, second.Status);
                return Array.Empty<CatalystExposureClassification>();
            }

            var secondText = second.GetOutputText();
            _logger.Trace("Catalyst LLM retry response — {0}: {1}", agentName, secondText);

            if (TryParse(secondText, validNames, agentName, out var retried, out var retryError))
                return LogVerdicts(fundName, retried);

            _logger.Warn("Catalyst LLM retry not parsed — {0}: {1}; defaulting to no catalyst. Raw: {2}",
                agentName, retryError, secondText);
            return Array.Empty<CatalystExposureClassification>();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Warn("Catalyst LLM call timed out — {0}, after {1}: defaulting to no catalyst", agentName, RunTimeout);
            return Array.Empty<CatalystExposureClassification>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn(ex, "Catalyst LLM call failed — {0}: defaulting to no catalyst", agentName);
            return Array.Empty<CatalystExposureClassification>();
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private IReadOnlyList<CatalystExposureClassification> LogVerdicts(
        string fundName, IReadOnlyList<CatalystExposureClassification> classifications)
    {
        if (classifications.Count == 0)
            _logger.Debug("Catalyst LLM verdict — fund '{0}': no classifications", fundName);

        foreach (var c in classifications)
            _logger.Debug("Catalyst LLM verdict — fund '{0}': {1} {2}, rationale: {3}",
                fundName, c.CatalystName, c.Exposure, c.Rationale ?? "-");

        return classifications;
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

    private bool TryParse(
        string raw,
        HashSet<string> validNames,
        string agentName,
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
                {
                    // The contract's hallucination guard: drop the entry, warn.
                    _logger.Warn("Catalyst LLM named unknown catalyst '{0}' — {1}: dropped", name, agentName);
                    continue;
                }

                if (!TryParseExposure(typeRaw, out var exposure))
                {
                    _logger.Warn("Catalyst LLM gave unknown exposure '{0}' for '{1}' — {2}: dropped",
                        typeRaw, name, agentName);
                    continue;
                }

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
        var definition = new DeclarativeAgentDefinition(_deploymentName.Value)
        {
            Instructions = SystemPrompt,
        };
        var safeModel = _deploymentName.Value.Replace(".", "-").Replace("_", "-");
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
            _logger.Trace("Catalyst LLM agent deleted — {0} v{1}", agentName, agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
        catch (ClientResultException ex) when (ex.Status == 404) { }
        catch (Exception ex)
        {
            // Best-effort cleanup; stragglers can be swept later.
            _logger.Warn(ex, "Catalyst LLM agent not deleted — {0} v{1}, left for a sweep", agentName, agentVersion);
        }
    }
}
