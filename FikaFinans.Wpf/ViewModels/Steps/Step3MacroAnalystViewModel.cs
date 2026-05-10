using System.IO;
using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step3MacroAnalystViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IMacroAnalystAgent? _agent;

    public override int StepNumber => 3;
    public override string AgentName => "Macro analyst";
    public override bool HasConfig => false;

    public Step3MacroAnalystViewModel() { }

    public Step3MacroAnalystViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IMacroAnalystAgent agent)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
    }

    protected override async Task RunStepCoreAsync()
    {
        if (_agent is null || _paths is null)
        {
            OutputSummaryText = "Configure Foundry credentials in Settings → Models";
            return;
        }
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        await _agent.RunAsync(IsoWeek, RunId);

        var outPath = _paths.MacroAnalystOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = "Macro analysis complete";
    }
}
