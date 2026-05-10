using System.IO;
using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step1DataLoaderViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IDataLoaderAgent? _agent;

    public override int StepNumber => 1;
    public override string AgentName => "Data loader";
    public override bool HasConfig => false;

    public Step1DataLoaderViewModel() { }

    public Step1DataLoaderViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IDataLoaderAgent agent)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
    }

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

        var output = await Task.Run(() => _agent.Run(Family, IsoWeek, RunId));

        var outPath = _paths.DataLoaderOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = $"{output.Funds.Count} funds loaded";
    }
}
