using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Models;
using System.ClientModel;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using OpenAI.Responses;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Llm.Foundry;

// Minimal Foundry-backed LLM client for MacroAnalyst. Mirrors the ephemeral-agent
// pattern used in FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents.ActionConsolidationAgent:
// create a named agent with the system prompt as Instructions, fire a single
// CreateResponseAsync, then delete the agent.
public sealed class FoundryMacroLlmClient : IMacroLlmClient
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);

    private readonly ILogger _logger;
    private readonly AIProjectClient _projectClient;
    private readonly FoundryDeploymentName _deploymentName;

    /// <param name="logger">Where the call, the response size and any failure are logged.</param>
    /// <param name="projectClient">The Foundry project the ephemeral agent is created in.</param>
    /// <param name="deploymentName">
    /// The Foundry deployment the agent runs on — the name the portal gave the deployment, not
    /// the model family: e.g. <c>gpt-5.4-1</c> or <c>DeepSeek-R1-0528-1</c>, see
    /// <see cref="Infrastructure.Foundry.FoundryModelIds"/>.
    /// </param>
    public FoundryMacroLlmClient(ILogger logger, AIProjectClient projectClient, FoundryDeploymentName deploymentName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName.Value, nameof(deploymentName));
        _logger = logger;
        _projectClient = projectClient;
        _deploymentName = deploymentName;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        _logger.Debug("Macro LLM call — model {0}, system {1} chars, user {2} chars, timeout {3}",
            _deploymentName, systemPrompt.Length, userPrompt.Length, RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(systemPrompt, timeoutCts.Token);
        _logger.Trace("Macro LLM agent created — {0} v{1}", agentName, agentVersion);
        try
        {
            var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);
            var responseWrapper = await responseClient.CreateResponseAsync(userPrompt, cancellationToken: timeoutCts.Token);
            var response = responseWrapper.Value;
            if (response.Status != ResponseStatus.Completed)
                throw new InvalidOperationException(
                    $"MacroAnalyst response ended with status {response.Status} for model {_deploymentName}");

            var text = response.GetOutputText();
            _logger.Debug("Macro LLM response — {0}, {1} chars", agentName, text.Length);
            return text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Warn("Macro LLM call timed out — {0}, after {1}", agentName, RunTimeout);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn(ex, "Macro LLM call failed — {0}", agentName);
            throw;
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    // Slowest part of the call: measured 2026-10-12 at 14 s to create the agent version vs 11 s
    // for the response itself (plus 3 s to delete). Acceptable while step 3 computes once per
    // week's reports; if it starts to matter, reuse one named agent per prompt version instead
    // of a create/delete per call.
    private async Task<(string Name, string Version)> CreateAgentAsync(string systemPrompt, CancellationToken ct)
    {
        var definition = new DeclarativeAgentDefinition(_deploymentName.Value)
        {
            Instructions = systemPrompt,
        };
        // Foundry agent names are capped at 63 chars and must start/end alphanumeric.
        var safeModel = _deploymentName.Value.Replace(".", "-").Replace("_", "-");
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
            _logger.Trace("Macro LLM agent deleted — {0} v{1}", agentName, agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
        catch (ClientResultException ex) when (ex.Status == 404) { }
        catch (Exception ex)
        {
            // Best-effort cleanup; the test base or operator can sweep stragglers later.
            _logger.Warn(ex, "Macro LLM agent not deleted — {0} v{1}, left for a sweep", agentName, agentVersion);
        }
    }
}
