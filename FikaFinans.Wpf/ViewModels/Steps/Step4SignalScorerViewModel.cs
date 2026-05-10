using System.IO;
using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
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

        var output = await Task.Run(() => _agent.Run(IsoWeek, RunId));

        var outPath = _paths.SignalScorerOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = $"{output.Funds.Count} funds — signals scored";
    }
}
