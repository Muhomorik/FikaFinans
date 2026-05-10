using System.IO;
using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step6CatalystTaggerViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly ICatalystTaggerAgent? _agent;

    public override int StepNumber => 6;
    public override string AgentName => "Catalyst tagger";
    public override bool HasConfig => false;

    public Step6CatalystTaggerViewModel() { }

    public Step6CatalystTaggerViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, ICatalystTaggerAgent agent)
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

        var output = await _agent.RunAsync(IsoWeek, RunId);

        var outPath = _paths.CatalystTaggerOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = $"{output.Funds.Count} funds — catalysts tagged";
    }
}
