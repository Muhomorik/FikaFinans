using System.IO;
using System.Reactive.Concurrency;
using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step2MetricsCalculatorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IMetricsCalculatorAgent? _agent;
    private readonly IIsinProgressRepository? _isinProgress;

    public override int StepNumber => 2;
    public override string AgentName => "Metrics calculator";
    public override bool HasConfig => true;

    public Step2MetricsCalculatorViewModel() { }

    public Step2MetricsCalculatorViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IMetricsCalculatorAgent agent, IConfigEditorDialogService configEditor,
        IIsinProgressRepository isinProgress)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _configEditorService = configEditor;
        _isinProgress = isinProgress;
    }

    protected override string? GetConfigPath() => _paths?.Config02MetricsJson;

    protected override async Task RunStepCoreAsync()
    {
        if (_agent is null || _paths is null)
        {
            OutputSummaryText = "Configure data folder in Settings → Folders";
            return;
        }
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        await Task.Run(() => _agent.Run(IsoWeek, RunId));
        await LoadOutputAsync();
    }

    public override async Task LoadOutputAsync()
    {
        // 8c: prefer SQLite Step02Json columns; disk fallback for per-step button flow.
        if (_isinProgress is not null)
        {
            var sqliteResult = await IsinProgressOutputLoader.LoadStepFundsAsync(
                _isinProgress, RunId, row => row.Step02Json);
            if (sqliteResult is not null)
            {
                OutputJson = sqliteResult.Json;
                OutputSummaryText = $"{sqliteResult.Funds.Count} funds — metrics calculated";
                return;
            }
        }

        if (_paths is null || string.IsNullOrEmpty(IsoWeek)) return;

        var outPath = _paths.MetricsCalculatorOutput(IsoWeek, RunId);
        if (!File.Exists(outPath))
        {
            OutputSummaryText = "Output file not found";
            return;
        }

        var json = await File.ReadAllTextAsync(outPath);
        OutputJson = json;

        var output = JsonSerializer.Deserialize<DataLoaderOutput>(json, JsonOptions.Default);
        OutputSummaryText = output is null
            ? "Output file present but unreadable"
            : $"{output.Funds.Count} funds — metrics calculated";
    }
}
