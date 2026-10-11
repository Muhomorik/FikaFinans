using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step3MacroAnalystViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IMacroAnalystAgent? _agent;
    private readonly IStep03MacroAnalyst? _step03;

    public override int StepNumber => 3;
    public override string AgentName => "Macro analyst";
    public override bool HasConfig => false;

    public Step3MacroAnalystViewModel() { }

    public Step3MacroAnalystViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IMacroAnalystAgent agent, IStep03MacroAnalyst step03,
        IStepEventSource stepEvents, IPipelineSignalStreams pipelineSignals)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _step03 = step03;

        // The tab follows the step rather than running it: whoever drove the handler —
        // step 2's done-signal locally, a queue trigger in Azure — reports through this stream.
        Disposables.Add(stepEvents.Events
            .Where(tick => tick.Step == StepId.MacroAnalyst)
            .ObserveOn(uiScheduler)
            .Subscribe(ApplyStepEvent));

        // The context is per week, not per run: the done-signal's trading date names the
        // week, and the tab shows the context a fund of that week runs with.
        Disposables.Add(pipelineSignals.Signals
            .OfType<Step03DoneSignal>()
            .ObserveOn(uiScheduler)
            .Select(done => Observable
                .FromAsync(() => LoadContextAsync(Domain.Pipeline.IsoWeek.FromDate(done.NavDate)))
                // Caught per load: an error reaching Subscribe would end the stream for
                // the session, and the next week's context would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 3 context load failed — runId={0}", done.RunId.Value);
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
    }

    /// <summary>
    /// The debug button: runs the agent file in, file out, and shows what it returned. It
    /// neither reads nor writes the context store — a cached context would not be
    /// debugging the LLM call, and a replaced one would change what every later fund reads.
    /// </summary>
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

        var context = await _agent.RunAsync(IsoWeek, RunId);
        Show(context, requestedWeek: IsoWeek, prefix: "Debug run, not stored");
    }

    /// <summary>
    /// Fails the runs orphaned at step 3, so the next NAV sync reprocesses those funds.
    /// </summary>
    protected override async Task OnFirstOpenedAsync()
    {
        if (_step03 is null) return;

        var failed = await _step03.FailOrphanedRunsAsync(SessionStartedAt);

        if (failed > 0)
            OutputSummaryText = $"{failed} orphaned run(s) failed — reprocessed on the next NAV sync";
    }

    /// <summary>Shows the stored context for the run bar's week.</summary>
    public override Task LoadOutputAsync()
        => string.IsNullOrEmpty(IsoWeek)
            ? Task.CompletedTask
            : LoadContextAsync(Domain.Pipeline.IsoWeek.From(IsoWeek));

    private async Task LoadContextAsync(Domain.Pipeline.IsoWeek week)
    {
        if (_step03 is null) return;

        var context = await _step03.ReadContextAsync(week);

        if (context is null)
        {
            OutputSummaryText = $"No macro context stored for {week} or earlier";
            return;
        }

        Show(context, requestedWeek: week.Value, prefix: null);
    }

    private void Show(MacroContext context, string requestedWeek, string? prefix)
    {
        OutputJson = JsonSerializer.Serialize(context, JsonOptions.Default);

        // Says so when the week shown is an earlier one — the fallback a fund of the
        // requested week would also be running on.
        var week = context.IsoWeek == requestedWeek
            ? context.IsoWeek
            : $"{context.IsoWeek} (no context for {requestedWeek} yet)";

        var summary =
            $"{week} — {context.MacroRegime}, {context.Catalysts.Count} catalyst(s), " +
            $"{context.RotationThemes.Count} rotation theme(s)";

        OutputSummaryText = prefix is null ? summary : $"{prefix} — {summary}";
    }
}
