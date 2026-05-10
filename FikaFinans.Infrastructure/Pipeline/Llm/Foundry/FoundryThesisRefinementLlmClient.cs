using FikaFinans.Application.Pipeline.Llm;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using OpenAI.Responses;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Foundry-backed thesis refinement. Mirrors steps 05 / 06: create an ephemeral
// declarative agent with the system prompt baked in, fire one
// CreateResponseAsync per fund, then delete the agent. Failures (invalid JSON
// after one retry, non-Completed status) collapse to the baseline — the agent
// turns those into matrix-method outputs with a default rationale.
public sealed class FoundryThesisRefinementLlmClient : IThesisRefinementLlmClient
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private const string SystemPrompt = """
        You are a thesis validator. You will be given a fund's signal, catalyst
        (if any), macro alignment, and a baseline thesis_validity from a decision
        matrix.

        Your job is to either CONFIRM the baseline or recommend an ADJACENT label
        (Valid ↔ Partial, Partial ↔ Invalid). You may not jump two steps.

        Constraints:
        - Your output MUST be valid JSON (no prose, no markdown fences):
          {"thesis_validity": "<label>", "rationale": "..."}
        - thesis_validity must be one of {Valid, Partial, Invalid, NotApplicable}.
        - rationale must be ≤2 sentences and specific to the inputs (cite the
          catalyst, signal, or macro alignment by name).
        - If the baseline is correct, restate it; do not invent a different label
          to seem useful.

        The most consequential pattern: signal=Weakness with active catalyst
        means the thesis is BROKEN even though the catalyst is firing. Price
        action overrides narrative — emit Invalid with a rationale that names
        the price-vs-narrative split.
        """;

    private const string RetryPromptPrefix = """
        Your previous response failed validation: {error}

        Re-emit valid JSON (object only, no prose, no fences). Stay within the
        thesis_validity enum {Valid, Partial, Invalid, NotApplicable}.

        Original request follows:
        """;

    private readonly AIProjectClient _projectClient;
    private readonly string _modelId;

    public FoundryThesisRefinementLlmClient(AIProjectClient projectClient, string modelId)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _projectClient = projectClient;
        _modelId = modelId;
    }

    public async Task<ThesisRefinementVerdict> RefineAsync(
        FundRecord fund,
        ThesisValidity baseline,
        CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var userPrompt = BuildUserPrompt(fund, baseline);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(agentName);

            string? error = null;
            var first = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            if (first.Value.Status == ResponseStatus.Completed
                && TryParse(first.Value.GetOutputText(), out var parsed, out error))
            {
                return parsed!;
            }

            var retryPrompt = RetryPromptPrefix.Replace("{error}", error ?? "invalid JSON")
                              + "\n" + userPrompt;
            var second = await responseClient.CreateResponseAsync(retryPrompt, cancellationToken: timeoutCts.Token);
            if (second.Value.Status == ResponseStatus.Completed
                && TryParse(second.Value.GetOutputText(), out var retried, out _))
            {
                return retried!;
            }

            return ThesisRefinementVerdict.ConfirmBaseline(baseline, FallbackRationale(baseline));
        }
        catch
        {
            return ThesisRefinementVerdict.ConfirmBaseline(baseline, FallbackRationale(baseline));
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private static string BuildUserPrompt(FundRecord fund, ThesisValidity baseline)
    {
        var sb = new StringBuilder();
        sb.Append("Fund: ").Append(fund.Metadata.Name)
          .Append(" (").Append(fund.Isin).AppendLine(")");
        sb.Append("Category: ").AppendLine(fund.Metadata.Category);
        sb.AppendLine();
        sb.Append("Signal: ").Append(fund.Signal?.ToString() ?? "null");
        if (!string.IsNullOrWhiteSpace(fund.RuleFired))
            sb.Append(" (").Append(fund.RuleFired).Append(')');
        sb.AppendLine();

        if (fund.Catalyst is null)
        {
            sb.AppendLine("Catalyst: none");
        }
        else
        {
            sb.Append("Catalyst: ").Append(fund.Catalyst.Name)
              .Append(" | exposure=").Append(fund.Catalyst.ExposureType)
              .Append(" | intensity=").Append(fund.Catalyst.Intensity)
              .Append(" | weeks_active=").Append(fund.Catalyst.WeeksActive)
              .AppendLine();
        }

        sb.Append("Macro alignment: ").AppendLine(fund.MacroAlignment?.ToString() ?? "None");
        sb.Append("Currently held: ").Append(fund.CurrentlyHeld).AppendLine();
        sb.AppendLine();
        sb.Append("Decision-matrix baseline thesis_validity: ").AppendLine(baseline.ToString());
        sb.AppendLine();
        sb.AppendLine("Confirm or adjust by one step. Return JSON object with rationale.");
        return sb.ToString();
    }

    private static bool TryParse(string raw, out ThesisRefinementVerdict? verdict, out string? error)
    {
        verdict = null;
        error = null;
        try
        {
            var json = JsonExtraction.ExtractFirstJsonObject(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var labelRaw = root.TryGetProperty("thesis_validity", out var l) ? l.GetString() : null;
            if (!TryParseLabel(labelRaw, out var label))
            {
                error = $"unknown thesis_validity '{labelRaw}'";
                return false;
            }

            var rationale = root.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;

            verdict = new ThesisRefinementVerdict
            {
                Validity  = label,
                Rationale = string.IsNullOrWhiteSpace(rationale) ? FallbackRationale(label) : rationale!,
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseLabel(string? raw, out ThesisValidity label)
    {
        if (string.Equals(raw, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            label = ThesisValidity.Valid; return true;
        }
        if (string.Equals(raw, "Partial", StringComparison.OrdinalIgnoreCase))
        {
            label = ThesisValidity.Partial; return true;
        }
        if (string.Equals(raw, "Invalid", StringComparison.OrdinalIgnoreCase))
        {
            label = ThesisValidity.Invalid; return true;
        }
        if (string.Equals(raw, "NotApplicable", StringComparison.OrdinalIgnoreCase))
        {
            label = ThesisValidity.NotApplicable; return true;
        }
        label = ThesisValidity.NotApplicable;
        return false;
    }

    private static string FallbackRationale(ThesisValidity baseline) => baseline switch
    {
        ThesisValidity.Valid         => "Matrix baseline retained — LLM refinement unavailable.",
        ThesisValidity.Partial       => "Matrix baseline retained — LLM refinement unavailable.",
        ThesisValidity.Invalid       => "Matrix baseline retained — LLM refinement unavailable.",
        ThesisValidity.NotApplicable => "No directional signal — no thesis to validate.",
        _                            => "Matrix baseline retained.",
    };

    private async Task<(string Name, string Version)> CreateAgentAsync(CancellationToken ct)
    {
        var definition = new DeclarativeAgentDefinition(_modelId)
        {
            Instructions = SystemPrompt,
        };
        var safeModel = _modelId.Replace(".", "-").Replace("_", "-");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 16);
        var agentName = $"FikaFinans-Step7-{safeModel}-{shortId}";

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
