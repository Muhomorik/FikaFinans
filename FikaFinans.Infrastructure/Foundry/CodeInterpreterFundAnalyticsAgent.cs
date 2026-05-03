using System.ClientModel;
using System.Diagnostics;

using Azure;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;

using FikaFinans.Application.Agents;
using FikaFinans.Domain.Models;

using NLog;

using OpenAI.Responses;

namespace FikaFinans.Infrastructure.Foundry;

/// <summary>
/// The single Foundry Code Interpreter agent adapter, parameterised at construction with
/// a <see cref="ModelId"/>. One DI registration per deployed model — only ModelId differs —
/// powers the side-by-side comparison.
/// </summary>
/// <remarks>
/// Run flow per call:
/// <list type="number">
///   <item>Ensure all canonical data files are uploaded via the OpenAI Files API (implicit, mtime-based).</item>
///   <item>Create an ephemeral declarative agent version bound to <see cref="ModelId"/> with a
///     Code Interpreter tool whose container config carries the resolved file IDs.</item>
///   <item>Invoke via the Responses API — synchronous (no thread/run polling).</item>
///   <item>Read text + token usage off <see cref="ResponseResult"/>.</item>
///   <item>Delete the agent version (cheap; uploaded files persist for the next call).</item>
/// </list>
/// </remarks>
public sealed class CodeInterpreterFundAnalyticsAgent : IFundAnalyticsAgent
{
    // Reasoning models (gpt-5.x, DeepSeek-R1) running Code Interpreter on a meaty prompt
    // can hold the Responses API call open for several minutes. Pair with the matching
    // NetworkTimeout in InfrastructureModule.BuildAIProjectClient so this token, not the
    // SDK's per-attempt cap, governs cancellation.
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);

    private readonly ILogger _logger;
    private readonly AIProjectClient _projectClient;
    private readonly IFoundryFileStore _fileStore;
    private readonly IPromptProvider _promptProvider;
    private readonly FundDataFileSet _fileSet;
    private readonly TimeProvider _timeProvider;

    public CodeInterpreterFundAnalyticsAgent(
        ILogger logger,
        AIProjectClient projectClient,
        IFoundryFileStore fileStore,
        IPromptProvider promptProvider,
        FundDataFileSet fileSet,
        TimeProvider timeProvider,
        string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _logger = logger;
        _projectClient = projectClient;
        _fileStore = fileStore;
        _promptProvider = promptProvider;
        _fileSet = fileSet;
        _timeProvider = timeProvider;
        ModelId = modelId;
    }

    public string ModelId { get; }

    public async Task<FundAnalyticsRun> RunAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var startedAt = _timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();

        var fileIds = await _fileStore.EnsureFilesUploadedAsync(_fileSet, ct);
        var prompt = _promptProvider.GetFundAnalyticsPrompt();

        // Foundry mounts each upload at /mnt/data/{fileId}-{originalFilename} — *not* just
        // /mnt/data/{originalFilename}. Compose the actual sandbox paths from the resolved
        // fileIds and substitute them into the prompt's {{*_PATH}} placeholders.
        static string SandboxPath(string fileId, string logicalName)
        {
            return $"/mnt/data/{fileId}-{logicalName}";
        }

        var systemPrompt = prompt.SystemPrompt
            .Replace("{{SUMMARY_PATH}}", SandboxPath(fileIds[FundDataFiles.Summary], FundDataFiles.Summary))
            .Replace("{{METADATA_PATH}}", SandboxPath(fileIds[FundDataFiles.Metadata], FundDataFiles.Metadata))
            .Replace("{{POSITIONS_PATH}}", SandboxPath(fileIds[FundDataFiles.Positions], FundDataFiles.Positions))
            .Replace("{{STRUCTURE_PATH}}", SandboxPath(fileIds[FundDataFiles.Structure], FundDataFiles.Structure))
            .Replace("{{ROTATION_TARGETS_PATH}}",
                SandboxPath(fileIds[FundDataFiles.AnalyticsRotationTargets], FundDataFiles.AnalyticsRotationTargets))
            .Replace("{{SUBSTITUTION_CHAIN_PATH}}",
                SandboxPath(fileIds[FundDataFiles.AnalyticsSubstitutionChain],
                    FundDataFiles.AnalyticsSubstitutionChain))
            .Replace("{{WEEKLY_SUMMARY_PATH}}",
                SandboxPath(fileIds[FundDataFiles.AnalyticsWeeklySummary], FundDataFiles.AnalyticsWeeklySummary));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(systemPrompt, fileIds.Values, timeoutCts.Token);

        try
        {
            var (responseText, inputTokens, outputTokens) = await RunAndCollectAsync(
                agentName, agentVersion, question, timeoutCts.Token);
            stopwatch.Stop();

            return new FundAnalyticsRun(
                ModelId,
                question,
                responseText,
                inputTokens,
                outputTokens,
                stopwatch.ElapsedMilliseconds,
                startedAt);
        }
        finally
        {
            await TryDeleteAgentAsync(agentName, agentVersion);
        }
    }

    private async Task<(string Name, string Version)> CreateAgentAsync(
        string systemPrompt,
        IEnumerable<string> fileIds,
        CancellationToken ct)
    {
        var agentDefinition = new DeclarativeAgentDefinition(ModelId)
        {
            Instructions = systemPrompt,
            Tools =
            {
                ResponseTool.CreateCodeInterpreterTool(
                    new CodeInterpreterToolContainer(
                        CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(
                            fileIds.ToArray())))
            }
        };

        // Foundry agent names are URI segments — no dots/underscores, just letters/digits/dashes.
        var safeModel = ModelId.Replace(".", "-").Replace("_", "-");
        var agentName = $"FikaFinans-{safeModel}-{Guid.NewGuid():N}";

        var response = await _projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
            agentName,
            new ProjectsAgentVersionCreationOptions(agentDefinition),
            cancellationToken: ct);

        _logger.Info("Created ephemeral Foundry agent {AgentName} v{Version} for model {ModelId}",
            response.Value.Name, response.Value.Version, ModelId);
        return (response.Value.Name, response.Value.Version);
    }

    private async Task<(string Text, int InputTokens, int OutputTokens)> RunAndCollectAsync(
        string agentName,
        string agentVersion,
        string question,
        CancellationToken ct)
    {
        // Ephemeral agent name is unique per call (UUID suffix), so the latest-version
        // resolution that GetProjectResponsesClientForAgent(name) does is exactly the version
        // we just created. The agentVersion parameter stays for the cleanup path.
        _ = agentVersion;
        var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);

        var responseWrapper = await responseClient.CreateResponseAsync(question, cancellationToken: ct);
        var response = responseWrapper.Value;

        if (response.Status != ResponseStatus.Completed)
        {
            _logger.Error("Run did not complete for model {ModelId}: status={Status}", ModelId, response.Status);
            throw new InvalidOperationException(
                $"Foundry response for {ModelId} ended with status {response.Status}");
        }

        var text = response.GetOutputText();
        var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
        var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
        return (text, inputTokens, outputTokens);
    }

    private async Task TryDeleteAgentAsync(string agentName, string agentVersion)
    {
        try
        {
            await _projectClient.AgentAdministrationClient.DeleteAgentVersionAsync(
                agentName,
                agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.Info("Ephemeral agent {AgentName} v{Version} already absent server-side — skipping delete",
                agentName, agentVersion);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            _logger.Info("Ephemeral agent {AgentName} v{Version} already absent server-side — skipping delete",
                agentName, agentVersion);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to delete ephemeral agent {AgentName} v{Version} — leaving for service cleanup",
                agentName, agentVersion);
        }
    }
}