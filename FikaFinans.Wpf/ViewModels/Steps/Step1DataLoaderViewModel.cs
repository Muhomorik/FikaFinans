using System.Reactive.Concurrency;
using System.Reactive.Linq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Pipeline;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step1DataLoaderViewModel : StepViewModel
{
    private readonly IDataLoaderAgent? _agent;
    private readonly IStep01DataLoader? _step01;

    public override int StepNumber => 1;
    public override string AgentName => "Data loader";
    public override bool HasConfig => false;

    public Step1DataLoaderViewModel() { }

    public Step1DataLoaderViewModel(ILogger logger, IScheduler uiScheduler,
        IDataLoaderAgent agent,
        IStep01DataLoader step01, IStepEventSource stepEvents,
        IPipelineSignalStreams pipelineSignals)
        : base(logger, uiScheduler)
    {
        _agent = agent;
        _step01 = step01;

        // The tab follows the step rather than running it: whoever drove the handler —
        // a signal locally, a queue trigger in Azure — reports through this stream.
        Disposables.Add(stepEvents.Events
            .Where(tick => tick.Step == StepId.DataLoader)
            .ObserveOn(uiScheduler)
            .Subscribe(ApplyStepEvent));

        // StepEvent carries no run id, and a per-ISIN run mints its own rather than using
        // the run bar's — so the output is read from the done-signal, which names the run.
        Disposables.Add(pipelineSignals.Signals
            .OfType<Step01DoneSignal>()
            .ObserveOn(uiScheduler)
            .Select(done => Observable
                .FromAsync(() => LoadOutputAsync(done.RunId))
                // Caught per load: an error reaching Subscribe would end the stream for
                // the session, and the next run's output would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 1 output load failed — runId={0}", done.RunId.Value);
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
    }

    protected override async Task RunStepCoreAsync()
    {
        // Null only under the designer's parameterless constructor.
        if (_agent is null)
            return;
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        await Task.Run(() => _agent.Run(Family, IsoWeek, RunId));
        await LoadOutputAsync();
    }

    public override Task LoadOutputAsync() => LoadOutputAsync(RunId);

    private async Task LoadOutputAsync(PipelineRunId runId)
    {
        if (_step01 is null) return;

        var output = await _step01.ReadOutputAsync(runId);

        if (output is null)
        {
            OutputSummaryText = "Nothing stored for this run yet";
            return;
        }

        OutputJson = output.Json;
        OutputSummaryText = $"{output.Funds.Count} funds loaded";
    }
}
