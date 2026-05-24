using System.IO;
using System.Reactive.Concurrency;
using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step4SignalScorerViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly ISignalScorerAgent? _agent;

    public override int StepNumber => 4;
    public override string AgentName => "Signal scorer";
    public override bool HasConfig => true;

    public Step4SignalScorerViewModel() { }

    public Step4SignalScorerViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, ISignalScorerAgent agent, IConfigEditorDialogService configEditor)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _configEditorService = configEditor;
    }

    protected override string? GetConfigPath() => _paths?.Config04SignalsJson;

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
        if (_paths is null || string.IsNullOrEmpty(IsoWeek)) return;

        var outPath = _paths.SignalScorerOutput(IsoWeek, RunId);
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
            : $"{output.Funds.Count} funds — signals scored";
    }
}
