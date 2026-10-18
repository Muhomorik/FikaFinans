using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.Json;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

using JetBrains.Annotations;

using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

/// <summary>
/// The step 8 tab: shows the run's recommended fund, following the step's events rather than
/// driving them, with a debug button that recommends for the run again without storing it.
/// </summary>
public sealed class Step8RecommenderViewModel : StepViewModel
{
    private readonly IStep08Recommender _step08;

    public override int StepNumber => 8;
    public override string AgentName => "Recommender";
    public override bool HasConfig => false;

    [UsedImplicitly]
    public Step8RecommenderViewModel() => _step08 = null!;   // designer only

    [UsedImplicitly]
    public Step8RecommenderViewModel(ILogger logger, IScheduler uiScheduler,
        IStep08Recommender step08,
        IStepEventSource stepEvents, IPipelineSignalStreams pipelineSignals)
        : base(logger, uiScheduler)
    {
        _step08 = step08;

        // The tab follows the step rather than running it: whoever drove the handler —
        // step 7's done-signal locally, a queue trigger in Azure — reports through this stream.
        Disposables.Add(stepEvents.Events
            .Where(tick => tick.Step == StepId.Recommender)
            .ObserveOn(uiScheduler)
            .Subscribe(ApplyStepEvent));

        // StepEvent carries no run id, and a per-ISIN run has its own rather than the run
        // bar's — so the output is read from step 8's done-signal, which names the run.
        Disposables.Add(pipelineSignals.Signals
            .OfType<Step08DoneSignal>()
            .ObserveOn(uiScheduler)
            .Select(done => Observable
                .FromAsync(() => LoadOutputAsync(done.RunId))
                // Caught per load: an error reaching Subscribe would end the stream for
                // the session, and the next run's output would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 8 output load failed — runId={0}", done.RunId.Value);
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
    }

    /// <summary>
    /// The debug button: recommends for the run bar's run through the step and shows the
    /// result. Never stored — a replaced output would change what step 9 reads. Free: the
    /// mapping calls no model.
    /// </summary>
    protected override async Task RunStepCoreAsync()
    {
        // Null only under the designer's parameterless constructor.
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        var output = await _step08.ComputeOutputAsync(RunId, Domain.Pipeline.IsoWeek.From(IsoWeek));

        // The funds only, the same shape a stored output reads back as.
        OutputJson = JsonSerializer.Serialize(output.Funds, JsonOptions.Default);
        OutputSummaryText = $"Debug run, not stored — {output.Funds.Count} funds — recommendations generated";
    }

    /// <summary>
    /// Fails the runs orphaned at step 8, so the next NAV sync reprocesses those funds.
    /// </summary>
    protected override async Task OnFirstOpenedAsync()
    {
        var failed = await _step08.FailOrphanedRunsAsync(SessionStartedAt);

        if (failed > 0)
            OutputSummaryText = $"{failed} orphaned run(s) failed — reprocessed on the next NAV sync";
    }

    public override Task LoadOutputAsync() => LoadOutputAsync(RunId);

    private async Task LoadOutputAsync(PipelineRunId runId)
    {
        var output = await _step08.ReadOutputAsync(runId);

        if (output is null)
        {
            OutputSummaryText = "Nothing stored for this run yet";
            return;
        }

        OutputJson = output.Json;
        OutputSummaryText = $"{output.Funds.Count} funds — recommendations generated";
    }
}
