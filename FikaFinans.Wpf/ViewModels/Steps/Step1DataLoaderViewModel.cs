using System.Reactive.Concurrency;
using System.Reactive.Linq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
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
        IStep01DataLoader step01, IStepEventSource stepEvents)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _step01 = step01;

        // The tab follows the step rather than running it: whoever drove the handler —
        // a signal locally, a queue trigger in Azure — reports through this stream.
        Disposables.Add(stepEvents.Events
            .Where(tick => tick.Step == StepId.DataLoader)
            .ObserveOn(uiScheduler)
            .Subscribe(Apply));
    }

    /// <summary>
    /// Projects one tick onto the tab's state, in the formats
    /// <c>MainWindowViewModel.OnStepEvent</c> already uses so both paths look the same.
    /// </summary>
    /// <remarks>
    /// Unlike that router, a populated <c>Isin</c> sets the status here rather than only
    /// bumping a counter: step 1 no longer has a universe-wide tick to carry it.
    /// </remarks>
    private void Apply(StepEvent tick)
    {
        switch (tick.Kind)
        {
            case StepEventKind.Started:
                Status = StepStatus.Running;
                IsRunning = true;
                HasError = false;
                ErrorText = string.Empty;
                break;

            case StepEventKind.Succeeded:
                Status = StepStatus.Ok;
                IsRunning = false;
                LastRunText = DateTime.Now.ToString("HH:mm:ss");
                if (tick.Duration is { } ok)
                    DurationText = $"{ok.TotalSeconds:N1} s";
                break;

            case StepEventKind.Failed:
                Status = StepStatus.Error;
                IsRunning = false;
                HasError = true;
                ErrorText = tick.Message ?? "unknown error";
                if (tick.Duration is { } failed)
                    DurationText = $"{failed.TotalSeconds:N1} s";
                break;
        }
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
