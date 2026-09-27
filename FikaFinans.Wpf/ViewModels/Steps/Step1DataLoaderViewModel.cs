using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Steps;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step1DataLoaderViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IDataLoaderAgent? _agent;
    private readonly IStep01DataLoader? _step01;

    public override int StepNumber => 1;
    public override string AgentName => "Data loader";
    public override bool HasConfig => false;

    public Step1DataLoaderViewModel() { }

    public Step1DataLoaderViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IDataLoaderAgent agent,
        IStep01DataLoader step01)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _step01 = step01;
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

        await Task.Run(() => _agent.Run(Family, IsoWeek, RunId));
        await LoadOutputAsync();
    }

    public override async Task LoadOutputAsync()
    {
        if (_step01 is null) return;

        var output = await _step01.ReadOutputAsync(RunId);

        if (output is null)
        {
            OutputSummaryText = "Nothing stored for this run yet";
            return;
        }

        OutputJson = output.Json;
        OutputSummaryText = $"{output.Funds.Count} funds loaded";
    }
}
