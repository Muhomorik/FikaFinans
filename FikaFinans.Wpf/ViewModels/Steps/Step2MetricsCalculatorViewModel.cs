using System.IO;
using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step2MetricsCalculatorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IMetricsCalculatorAgent? _agent;

    public override int StepNumber => 2;
    public override string AgentName => "Metrics calculator";
    public override bool HasConfig => true;

    public Step2MetricsCalculatorViewModel() { }

    public Step2MetricsCalculatorViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IMetricsCalculatorAgent agent, IConfigEditorDialogService configEditor)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _configEditorService = configEditor;
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

        var output = await Task.Run(() => _agent.Run(IsoWeek, RunId));

        var outPath = _paths.MetricsCalculatorOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = $"{output.Funds.Count} funds — metrics calculated";
    }
}
