using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Models;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using OpenAI.Responses;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Foundry-backed thesis refinement. Mirrors steps 05 / 06: create an ephemeral
// declarative agent with the system prompt baked in, fire one
// CreateResponseAsync per fund, then delete the agent. Failures (invalid JSON
// after one retry, non-Completed status, timeout) collapse to the baseline with a
// fallback rationale — so each one is logged here at Warn: to the caller it looks
// like a real confirmation.
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

    private readonly ILogger _logger;
    private readonly AIProjectClient _projectClient;
    private readonly FoundryDeploymentName _deploymentName;

    /// <param name="logger">Where the call, the verdict and why a failure became "baseline confirmed" are logged.</param>
    /// <param name="projectClient">The Foundry project the ephemeral agent is created in.</param>
    /// <param name="deploymentName">
    /// The Foundry deployment the agent runs on — the name the portal gave the deployment, not
    /// the model family: e.g. <c>gpt-5.4-1</c> or <c>DeepSeek-R1-0528-1</c>, see
    /// <see cref="Infrastructure.Foundry.FoundryModelIds"/>.
    /// </param>
    public FoundryThesisRefinementLlmClient(ILogger logger, AIProjectClient projectClient, FoundryDeploymentName deploymentName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName.Value, nameof(deploymentName));
        _logger = logger;
        _projectClient = projectClient;
        _deploymentName = deploymentName;
    }

    public async Task<ThesisRefinementVerdict> RefineAsync(
        FundRecord fund,
        ThesisValidity baseline,
        CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var userPrompt = BuildUserPrompt(fund, baseline);

        _logger.Debug("Thesis LLM call — model {0}, fund {1} '{2}', baseline {3}, timeout {4}",
            _deploymentName, fund.Isin, fund.Metadata.Name, baseline, RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        _logger.Trace("Thesis LLM agent created — {0} v{1}", agentName, agentVersion);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(agentName);

            string? error;
            var first = (await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token)).Value;
            if (first.Status != ResponseStatus.Completed)
            {
                // Unlike step 6's client, an incomplete first answer still gets the retry.
                error = $"response status {first.Status}";
                _logger.Warn("Thesis LLM response not completed — {0}, status {1}; retrying once",
                    agentName, first.Status);
            }
            else
            {
                var firstText = first.GetOutputText();
                _logger.Trace("Thesis LLM response — {0}: {1}", agentName, firstText);

                if (TryParse(firstText, out var parsed, out error))
                    return LogVerdict(fund, baseline, parsed!);

                // One corrective retry per the contract — an unknown label lands here too.
                _logger.Warn("Thesis LLM answer not parsed — {0}: {1}; retrying once. Raw: {2}",
                    agentName, error, firstText);
            }

            var retryPrompt = RetryPromptPrefix.Replace("{error}", error ?? "invalid JSON")
                              + "\n" + userPrompt;
            var second = (await responseClient.CreateResponseAsync(retryPrompt, cancellationToken: timeoutCts.Token)).Value;
            if (second.Status != ResponseStatus.Completed)
            {
                _logger.Warn("Thesis LLM retry not completed — {0}, status {1}: keeping baseline {2}",
                    agentName, second.Status, baseline);
                return ThesisRefinementVerdict.ConfirmBaseline(baseline, FallbackRationale(baseline));
            }

            var secondText = second.GetOutputText();
            _logger.Trace("Thesis LLM retry response — {0}: {1}", agentName, secondText);

            if (TryParse(secondText, out var retried, out var retryError))
                return LogVerdict(fund, baseline, retried!);

            _logger.Warn("Thesis LLM retry not parsed — {0}: {1}; keeping baseline {2}. Raw: {3}",
                agentName, retryError, baseline, secondText);
            return ThesisRefinementVerdict.ConfirmBaseline(baseline, FallbackRationale(baseline));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Warn("Thesis LLM call timed out — {0}, after {1}: keeping baseline {2}",
                agentName, RunTimeout, baseline);
            return ThesisRefinementVerdict.ConfirmBaseline(baseline, FallbackRationale(baseline));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn(ex, "Thesis LLM call failed — {0}: keeping baseline {1}", agentName, baseline);
            return ThesisRefinementVerdict.ConfirmBaseline(baseline, FallbackRationale(baseline));
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private ThesisRefinementVerdict LogVerdict(FundRecord fund, ThesisValidity baseline, ThesisRefinementVerdict verdict)
    {
        _logger.Debug("Thesis LLM verdict — fund {0}: baseline {1} → {2}, rationale: {3}",
            fund.Isin, baseline, verdict.Validity, verdict.Rationale);
        return verdict;
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
        var definition = new DeclarativeAgentDefinition(_deploymentName.Value)
        {
            Instructions = SystemPrompt,
        };
        var safeModel = _deploymentName.Value.Replace(".", "-").Replace("_", "-");
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
            _logger.Trace("Thesis LLM agent deleted — {0} v{1}", agentName, agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
        catch (ClientResultException ex) when (ex.Status == 404) { }
        catch (Exception ex)
        {
            // Best-effort cleanup; stragglers can be swept later.
            _logger.Warn(ex, "Thesis LLM agent not deleted — {0} v{1}, left for a sweep", agentName, agentVersion);
        }
    }
}
