using FikaFinans.Application.Pipeline.Llm;
using System.ClientModel;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using OpenAI.Responses;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Minimal Foundry-backed LLM client for MacroAnalyst. Mirrors the ephemeral-agent
// pattern used in FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents.ActionConsolidationAgent:
// create a named agent with the system prompt as Instructions, fire a single
// CreateResponseAsync, then delete the agent.
public sealed class FoundryMacroLlmClient : IMacroLlmClient
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);

    private readonly AIProjectClient _projectClient;
    private readonly string _modelId;

    public FoundryMacroLlmClient(AIProjectClient projectClient, string modelId)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _projectClient = projectClient;
        _modelId = modelId;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(systemPrompt, timeoutCts.Token);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);
            var responseWrapper = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            var response = responseWrapper.Value;
            if (response.Status != ResponseStatus.Completed)
                throw new InvalidOperationException(
                    $"MacroAnalyst response ended with status {response.Status} for model {_modelId}");

            return response.GetOutputText();
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private async Task<(string Name, string Version)> CreateAgentAsync(string systemPrompt, CancellationToken ct)
    {
        var definition = new DeclarativeAgentDefinition(_modelId)
        {
            Instructions = systemPrompt,
        };
        // Foundry agent names are capped at 63 chars and must start/end alphanumeric.
        var safeModel = _modelId.Replace(".", "-").Replace("_", "-");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 16);
        var agentName = $"FikaFinans-Step3-{safeModel}-{shortId}";

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
            // Best-effort cleanup; the test base or operator can sweep stragglers later.
        }
    }
}
