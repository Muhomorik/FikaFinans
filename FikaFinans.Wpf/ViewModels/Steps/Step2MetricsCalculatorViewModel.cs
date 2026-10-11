using System.Reactive.Concurrency;
using System.Reactive.Linq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step2MetricsCalculatorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IMetricsCalculatorAgent? _agent;
    private readonly IStep02MetricsCalculator? _step02;

    public override int StepNumber => 2;
    public override string AgentName => "Metrics calculator";
    public override bool HasConfig => true;

    public Step2MetricsCalculatorViewModel() { }

    public Step2MetricsCalculatorViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IMetricsCalculatorAgent agent, IConfigEditorDialogService configEditor,
        IStep02MetricsCalculator step02, IStepEventSource stepEvents,
        IPipelineSignalStreams pipelineSignals)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _configEditorService = configEditor;
        _step02 = step02;

        // The tab follows the step rather than running it: whoever drove the handler —
        // step 1's done-signal locally, a queue trigger in Azure — reports through this stream.
        Disposables.Add(stepEvents.Events
            .Where(tick => tick.Step == StepId.MetricsCalculator)
            .ObserveOn(uiScheduler)
            .Subscribe(ApplyStepEvent));

        // StepEvent carries no run id, and a per-ISIN run has its own rather than the run
        // bar's — so the output is read from step 2's done-signal, which names the run.
        Disposables.Add(pipelineSignals.Signals
            .OfType<Step02DoneSignal>()
            .ObserveOn(uiScheduler)
            .Select(done => Observable
                .FromAsync(() => LoadOutputAsync(done.RunId))
                // Caught per load: an error reaching Subscribe would end the stream for
                // the session, and the next run's output would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 2 output load failed — runId={0}", done.RunId.Value);
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
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

        await Task.Run(() => _agent.Run(IsoWeek, RunId));
        await LoadOutputAsync();
    }

    /// <summary>
    /// Fails the runs orphaned at step 2, so the next NAV sync reprocesses those funds.
    /// </summary>
    protected override async Task OnFirstOpenedAsync()
    {
        if (_step02 is null) return;

        var failed = await _step02.FailOrphanedRunsAsync(SessionStartedAt);

        if (failed > 0)
            OutputSummaryText = $"{failed} orphaned run(s) failed — reprocessed on the next NAV sync";
    }

    public override Task LoadOutputAsync() => LoadOutputAsync(RunId);

    private async Task LoadOutputAsync(PipelineRunId runId)
    {
        if (_step02 is null) return;

        var output = await _step02.ReadOutputAsync(runId);

        if (output is null)
        {
            OutputSummaryText = "Nothing stored for this run yet";
            return;
        }

        OutputJson = output.Json;
        OutputSummaryText = $"{output.Funds.Count} funds — metrics calculated";
    }
}
