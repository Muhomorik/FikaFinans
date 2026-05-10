using FikaFinans.Application.Pipeline.Llm;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using FikaFinans.Domain.Funds;
using OpenAI.Responses;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Foundry-backed differentiator writer for step 09. Mirrors FoundryFundCatalystLlmClient
// (step 06): create an ephemeral declarative agent with the system prompt baked in,
// fire one CreateResponseAsync, one corrective retry on invalid JSON, then delete the agent.
// Failures collapse to an empty list — the agent fills in empty strings and warns.
public sealed class FoundryDifferentiatorLlmClient : IDifferentiatorLlmClient
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private const string SystemPrompt = """
        You are a fund comparison analyst. You receive a primary fund that is a buy
        candidate and a list of alternative funds in the same category. For each
        alternative, write one short sentence (≤25 words) explaining what makes the
        alternative different from the primary — in terms of strategy, risk/return
        profile, fee, or manager focus.

        Return ONLY a JSON array (no prose, no markdown fences):
        [
          {"isin": "...", "differentiator": "..."},
          ...
        ]

        One entry per alternative, in the same order they appear in the input.
        Use only the ISINs from the input — do not invent new ones.
        """;

    private const string RetryPromptPrefix = """
        Your previous response failed validation: {error}

        Re-emit valid JSON (array only, no prose, no fences). Use only the ISINs
        from the input alternatives. One object per alternative:
        [{"isin": "...", "differentiator": "..."}, ...]

        Original request follows:
        """;

    private readonly AIProjectClient _projectClient;
    private readonly string _modelId;

    public FoundryDifferentiatorLlmClient(AIProjectClient projectClient, string modelId)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _projectClient = projectClient;
        _modelId = modelId;
    }

    public async Task<IReadOnlyList<DifferentiatorLine>> WriteDifferentiatorsAsync(
        DifferentiatorRequest request,
        CancellationToken ct = default)
    {
        if (request.Alternatives.Count == 0)
            return Array.Empty<DifferentiatorLine>();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var validIsins = request.Alternatives.Select(a => a.Isin.Value).ToHashSet(StringComparer.Ordinal);
        var userPrompt = BuildUserPrompt(request.Primary, request.Alternatives);

        var (agentName, agentVersion) = await CreateAgentAsync(timeoutCts.Token);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(agentName);

            var first = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            if (first.Value.Status == ResponseStatus.Completed)
            {
                if (TryParse(first.Value.GetOutputText(), validIsins, out var parsed, out var error))
                    return parsed;

                // One corrective retry per the contract.
                var retryPrompt = RetryPromptPrefix.Replace("{error}", error ?? "invalid JSON")
                                  + "\n" + userPrompt;
                var second = await responseClient.CreateResponseAsync(retryPrompt, cancellationToken: timeoutCts.Token);
                if (second.Value.Status == ResponseStatus.Completed
                    && TryParse(second.Value.GetOutputText(), validIsins, out var retried, out _))
                {
                    return retried;
                }
            }

            return Array.Empty<DifferentiatorLine>();
        }
        catch
        {
            return Array.Empty<DifferentiatorLine>();
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private static string BuildUserPrompt(FundRecord primary, IReadOnlyList<FundRecord> alternatives)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Primary fund (buy candidate):");
        sb.Append("- isin: ").AppendLine(primary.Isin.Value);
        sb.Append("- name: ").AppendLine(primary.Metadata.Name);
        sb.Append("- category: ").AppendLine(primary.Metadata.Category);
        if (primary.Metrics?.Sharpe12w is { } sharpe)
            sb.Append("- sharpe_12w: ").AppendLine(sharpe.ToString("F4"));
        sb.Append("- total_fee_pct: ").AppendLine(primary.Metadata.TotalFee.ToString("F2"));
        sb.AppendLine();
        sb.AppendLine("Alternatives (same category, ordered by sharpe descending):");
        foreach (var alt in alternatives)
        {
            sb.Append("- isin: ").Append(alt.Isin)
              .Append(" | name: ").Append(alt.Metadata.Name)
              .Append(" | sharpe_12w: ").Append(alt.Metrics?.Sharpe12w?.ToString("F4") ?? "n/a")
              .Append(" | fee: ").AppendLine(alt.Metadata.TotalFee.ToString("F2"));
        }
        sb.AppendLine();
        sb.AppendLine("Write one short differentiator per alternative vs the primary. Return JSON array.");
        return sb.ToString();
    }

    private static bool TryParse(
        string raw,
        HashSet<string> validIsins,
        out IReadOnlyList<DifferentiatorLine> result,
        out string? error)
    {
        result = Array.Empty<DifferentiatorLine>();
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

            var list = new List<DifferentiatorLine>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var isin = element.TryGetProperty("isin", out var i) ? i.GetString() : null;
                var differentiator = element.TryGetProperty("differentiator", out var d) ? d.GetString() : null;

                if (string.IsNullOrEmpty(isin) || !validIsins.Contains(isin))
                    continue; // hallucinated ISIN → drop

                list.Add(new DifferentiatorLine
                {
                    Isin           = isin,
                    Differentiator = differentiator ?? string.Empty,
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
        var agentName = $"FikaFinans-Step9-{safeModel}-{shortId}";

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
