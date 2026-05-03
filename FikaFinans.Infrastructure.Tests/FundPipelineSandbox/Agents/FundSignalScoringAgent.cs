using System.ClientModel;
using System.Diagnostics;

using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;

using FikaFinans.Application.Agents;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

using NLog;

using OpenAI.Responses;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;

/// <summary>
/// Step 5 sandbox agent — applies buy/sell rules + catalyst override + thesis
/// validity to every fund in the fixture's <c>summary.csv</c>. Uploads all
/// seven canonical files via Code Interpreter and asks the model to emit one
/// JSON object matching <see cref="FundSignalsRun"/>.
/// </summary>
/// <remarks>
/// Mirrors <c>FikaFinans.Infrastructure.Foundry.CodeInterpreterFundAnalyticsAgent</c>:
/// ephemeral declarative agent → invoke via Responses API → delete agent. Differs
/// in that it pulls files from a per-run <see cref="SandboxFileUploader"/> instead
/// of the production <c>FoundryFileStore</c> (no sidecar, fresh upload per test).
/// </remarks>
public sealed class FundSignalScoringAgent
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);

    private readonly AIProjectClient _projectClient;
    private readonly ILogger _logger;
    private readonly string _modelId;
    private readonly string _promptTemplate;

    public FundSignalScoringAgent(
        AIProjectClient projectClient,
        string modelId,
        string promptTemplate,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptTemplate);

        _projectClient = projectClient;
        _modelId = modelId;
        _promptTemplate = promptTemplate;
        _logger = logger ?? LogManager.GetLogger(nameof(FundSignalScoringAgent));
    }

    public string ModelId => _modelId;

    public async Task<FundSignalsRunResult> RunAsync(
        Step5Inputs inputs,
        SandboxFileUploader uploader,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(uploader);

        if (!Directory.Exists(inputs.FixtureFolder))
            throw new DirectoryNotFoundException($"fixture folder missing: {inputs.FixtureFolder}");

        var localPaths = FundDataFiles.All
            .Select(name => Path.Combine(inputs.FixtureFolder, name))
            .ToArray();

        var fileIds = await uploader.UploadAsync(localPaths, ct);

        // Foundry mounts each upload at /mnt/data/{fileId}-{originalFilename}.
        static string SandboxPath(string fileId, string basename) =>
            $"/mnt/data/{fileId}-{basename}";

        var systemPrompt = _promptTemplate
            .Replace("{{SUMMARY_PATH}}", SandboxPath(fileIds[FundDataFiles.Summary], FundDataFiles.Summary))
            .Replace("{{METADATA_PATH}}", SandboxPath(fileIds[FundDataFiles.Metadata], FundDataFiles.Metadata))
            .Replace("{{POSITIONS_PATH}}", SandboxPath(fileIds[FundDataFiles.Positions], FundDataFiles.Positions))
            .Replace("{{STRUCTURE_PATH}}", SandboxPath(fileIds[FundDataFiles.Structure], FundDataFiles.Structure))
            .Replace("{{ROTATION_TARGETS_PATH}}",
                SandboxPath(fileIds[FundDataFiles.AnalyticsRotationTargets], FundDataFiles.AnalyticsRotationTargets))
            .Replace("{{SUBSTITUTION_CHAIN_PATH}}",
                SandboxPath(fileIds[FundDataFiles.AnalyticsSubstitutionChain], FundDataFiles.AnalyticsSubstitutionChain))
            .Replace("{{WEEKLY_SUMMARY_PATH}}",
                SandboxPath(fileIds[FundDataFiles.AnalyticsWeeklySummary], FundDataFiles.AnalyticsWeeklySummary));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(systemPrompt, fileIds.Values, timeoutCts.Token);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);

            var responseWrapper = await responseClient.CreateResponseAsync(
                "Run the fund signal scoring per the system instructions and emit the JSON only.",
                cancellationToken: timeoutCts.Token);
            stopwatch.Stop();

            var response = responseWrapper.Value;
            if (response.Status != ResponseStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Step 5 response ended with status {response.Status} for model {_modelId}");
            }

            var rawText = response.GetOutputText();
            string? json = null;
            try
            {
                json = JsonResponse.ExtractFirstJsonObject(rawText);
                var run = JsonResponse.DeserializeOrThrow<FundSignalsRun>(json);

                return new FundSignalsRunResult(
                    Run: run,
                    RawResponseText: rawText,
                    ExtractedJson: json,
                    ModelId: _modelId,
                    InputTokens: (int)(response.Usage?.InputTokenCount ?? 0),
                    OutputTokens: (int)(response.Usage?.OutputTokenCount ?? 0),
                    ElapsedMs: stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not SandboxResponseParseException)
            {
                throw new SandboxResponseParseException(
                    $"Step 5 response parse failed for model {_modelId}: {ex.Message}",
                    rawText, json, ex);
            }
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
        var agentDefinition = new DeclarativeAgentDefinition(_modelId)
        {
            Instructions = systemPrompt,
            Tools =
            {
                ResponseTool.CreateCodeInterpreterTool(
                    new CodeInterpreterToolContainer(
                        CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(
                            fileIds.ToArray()))),
            },
        };

        // Foundry agent names are URI segments — letters/digits/dashes only.
        var safeModel = _modelId.Replace(".", "-").Replace("_", "-");
        var agentName = $"FikaFinans-Step5-{safeModel}-{Guid.NewGuid():N}";

        var response = await _projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
            agentName,
            new ProjectsAgentVersionCreationOptions(agentDefinition),
            cancellationToken: ct);

        _logger.Info("Created ephemeral Step 5 agent {AgentName} v{Version} for model {ModelId}",
            response.Value.Name, response.Value.Version, _modelId);
        return (response.Value.Name, response.Value.Version);
    }

    private async Task TryDeleteAgentAsync(string agentName, string agentVersion)
    {
        try
        {
            await _projectClient.AgentAdministrationClient.DeleteAgentVersionAsync(agentName, agentVersion);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone — nothing to do.
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            // Same — already gone.
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to delete ephemeral Step 5 agent {AgentName} v{Version}", agentName, agentVersion);
        }
    }
}
