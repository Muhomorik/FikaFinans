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
/// The step 7 tab: shows the run's validated fund, following the step's events rather than
/// driving them, with a debug button that validates the run again without storing it.
/// </summary>
public sealed class Step7ThesisValidatorViewModel : StepViewModel
{
    private readonly IStep07ThesisValidator _step07;

    public override int StepNumber => 7;
    public override string AgentName => "Thesis validator";
    public override bool HasConfig => false;

    [UsedImplicitly]
    public Step7ThesisValidatorViewModel() => _step07 = null!;   // designer only

    [UsedImplicitly]
    public Step7ThesisValidatorViewModel(ILogger logger, IScheduler uiScheduler,
        IStep07ThesisValidator step07,
        IStepEventSource stepEvents, IPipelineSignalStreams pipelineSignals)
        : base(logger, uiScheduler)
    {
        _step07 = step07;

        // The tab follows the step rather than running it: whoever drove the handler —
        // step 6's done-signal locally, a queue trigger in Azure — reports through this stream.
        Disposables.Add(stepEvents.Events
            .Where(tick => tick.Step == StepId.ThesisValidator)
            .ObserveOn(uiScheduler)
            .Subscribe(ApplyStepEvent));

        // StepEvent carries no run id, and a per-ISIN run has its own rather than the run
        // bar's — so the output is read from step 7's done-signal, which names the run.
        Disposables.Add(pipelineSignals.Signals
            .OfType<Step07DoneSignal>()
            .ObserveOn(uiScheduler)
            .Select(done => Observable
                .FromAsync(() => LoadOutputAsync(done.RunId))
                // Caught per load: an error reaching Subscribe would end the stream for
                // the session, and the next run's output would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 7 output load failed — runId={0}", done.RunId.Value);
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
    }

    /// <summary>
    /// The debug button: validates the run bar's run through the step and shows the result.
    /// Never stored — a replaced output would change what step 8 reads. Spends money only on
    /// funds that land on a conflicting matrix row.
    /// </summary>
    protected override async Task RunStepCoreAsync()
    {
        // Null only under the designer's parameterless constructor.
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        var output = await _step07.ComputeOutputAsync(RunId, Domain.Pipeline.IsoWeek.From(IsoWeek));

        // The funds only, the same shape a stored output reads back as.
        OutputJson = JsonSerializer.Serialize(output.Funds, JsonOptions.Default);
        OutputSummaryText = $"Debug run, not stored — {output.Funds.Count} funds — theses validated";
    }

    /// <summary>
    /// Fails the runs orphaned at step 7, so the next NAV sync reprocesses those funds.
    /// </summary>
    protected override async Task OnFirstOpenedAsync()
    {
        var failed = await _step07.FailOrphanedRunsAsync(SessionStartedAt);

        if (failed > 0)
            OutputSummaryText = $"{failed} orphaned run(s) failed — reprocessed on the next NAV sync";
    }

    public override Task LoadOutputAsync() => LoadOutputAsync(RunId);

    private async Task LoadOutputAsync(PipelineRunId runId)
    {
        var output = await _step07.ReadOutputAsync(runId);

        if (output is null)
        {
            OutputSummaryText = "Nothing stored for this run yet";
            return;
        }

        OutputJson = output.Json;
        OutputSummaryText = $"{output.Funds.Count} funds — theses validated";
    }
}
