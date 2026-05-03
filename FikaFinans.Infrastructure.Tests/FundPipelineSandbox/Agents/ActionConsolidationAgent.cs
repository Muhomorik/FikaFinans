using System.ClientModel;
using System.Diagnostics;
using System.Globalization;

using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;

using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

using NLog;

using OpenAI.Responses;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;

/// <summary>
/// Step 6 sandbox agent — converts the Step 5 fund-signals JSON into a single
/// ranked action list with capital math. No file uploads, no Code Interpreter:
/// the signals JSON, positions CSV, and portfolio structure markdown are read
/// from disk and inlined into the prompt.
/// </summary>
public sealed class ActionConsolidationAgent
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    private readonly AIProjectClient _projectClient;
    private readonly ILogger _logger;
    private readonly string _modelId;
    private readonly string _promptTemplate;

    public ActionConsolidationAgent(
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
        _logger = logger ?? LogManager.GetLogger(nameof(ActionConsolidationAgent));
    }

    public string ModelId => _modelId;

    public async Task<ActionConsolidationRunResult> RunAsync(
        Step6Inputs inputs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (!File.Exists(inputs.SignalsJsonPath))
            throw new FileNotFoundException($"missing signals JSON: {inputs.SignalsJsonPath}", inputs.SignalsJsonPath);
        if (!File.Exists(inputs.PositionsCsvPath))
            throw new FileNotFoundException($"missing positions.csv: {inputs.PositionsCsvPath}", inputs.PositionsCsvPath);
        if (!File.Exists(inputs.PortfolioStructurePath))
            throw new FileNotFoundException(
                $"missing portfolio_structure.md: {inputs.PortfolioStructurePath}", inputs.PortfolioStructurePath);

        var signalsJson = await File.ReadAllTextAsync(inputs.SignalsJsonPath, ct);
        var positionsCsv = await File.ReadAllTextAsync(inputs.PositionsCsvPath, ct);
        var structureMd = await File.ReadAllTextAsync(inputs.PortfolioStructurePath, ct);

        var systemPrompt = _promptTemplate
            .Replace("{{SIGNALS_JSON}}", signalsJson)
            .Replace("{{POSITIONS_CSV}}", positionsCsv)
            .Replace("{{PORTFOLIO_STRUCTURE_MD}}", structureMd)
            .Replace("{{CASH_AVAILABLE_KR}}", inputs.CashAvailableKr.ToString("0.##", CultureInfo.InvariantCulture));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        var (agentName, agentVersion) = await CreateAgentAsync(systemPrompt, timeoutCts.Token);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var responseClient = _projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentName);

            var responseWrapper = await responseClient.CreateResponseAsync(
                "Run the action consolidation per the system instructions and emit the JSON only.",
                cancellationToken: timeoutCts.Token);
            stopwatch.Stop();

            var response = responseWrapper.Value;
            if (response.Status != ResponseStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Step 6 response ended with status {response.Status} for model {_modelId}");
            }

            var rawText = response.GetOutputText();
            string? json = null;
            try
            {
                json = JsonResponse.ExtractFirstJsonObject(rawText);
                var run = JsonResponse.DeserializeOrThrow<ActionConsolidationRun>(json);

                return new ActionConsolidationRunResult(
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
                    $"Step 6 response parse failed for model {_modelId}: {ex.Message}",
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
        CancellationToken ct)
    {
        // Step 6 has no tools — pure transformation, no file access.
        var agentDefinition = new DeclarativeAgentDefinition(_modelId)
        {
            Instructions = systemPrompt,
        };

        var safeModel = _modelId.Replace(".", "-").Replace("_", "-");
        var agentName = $"FikaFinans-Step6-{safeModel}-{Guid.NewGuid():N}";

        var response = await _projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
            agentName,
            new ProjectsAgentVersionCreationOptions(agentDefinition),
            cancellationToken: ct);

        _logger.Info("Created ephemeral Step 6 agent {AgentName} v{Version} for model {ModelId}",
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
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to delete ephemeral Step 6 agent {AgentName} v{Version}", agentName, agentVersion);
        }
    }
}
