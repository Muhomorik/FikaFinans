using System.Diagnostics;
using System.Reactive.Subjects;
using FikaFinans.Application.Pipeline.Agents;
using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Sequential orchestrator for the 10 pipeline agents. First Phase 1 slice
/// per <c>Docs/pipeline-step-flow-plan.md</c>: matches today's WPF "Run All"
/// behaviour (universe-wide per-step calls, halt on first failure) but moves
/// the loop out of the WPF VM and emits <see cref="StepEvent"/>s so callers
/// can subscribe. Per-ISIN streaming and the per-fund tick on
/// <see cref="StepEvent"/> are follow-up slices.
/// </summary>
public sealed class PipelineRunner : IPipelineRunner, IDisposable
{
    private readonly ILogger _logger;
    private readonly IDataLoaderAgent _dataLoader;
    private readonly IMetricsCalculatorAgent _metrics;
    private readonly IMacroAnalystAgent _macroAnalyst;
    private readonly ISignalScorerAgent _signal;
    private readonly IMacroAlignerAgent _macroAligner;
    private readonly ICatalystTaggerAgent _catalyst;
    private readonly IThesisValidatorAgent _thesis;
    private readonly IRecommenderAgent _recommender;
    private readonly IUniverseEnricherAgent _enricher;
    private readonly IPortfolioConstructorAgent _portfolio;

    private readonly Subject<StepEvent> _events = new();

    public PipelineRunner(
        ILogger logger,
        IDataLoaderAgent dataLoader,
        IMetricsCalculatorAgent metrics,
        IMacroAnalystAgent macroAnalyst,
        ISignalScorerAgent signal,
        IMacroAlignerAgent macroAligner,
        ICatalystTaggerAgent catalyst,
        IThesisValidatorAgent thesis,
        IRecommenderAgent recommender,
        IUniverseEnricherAgent enricher,
        IPortfolioConstructorAgent portfolio)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dataLoader);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(macroAnalyst);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(macroAligner);
        ArgumentNullException.ThrowIfNull(catalyst);
        ArgumentNullException.ThrowIfNull(thesis);
        ArgumentNullException.ThrowIfNull(recommender);
        ArgumentNullException.ThrowIfNull(enricher);
        ArgumentNullException.ThrowIfNull(portfolio);

        _logger = logger;
        _dataLoader = dataLoader;
        _metrics = metrics;
        _macroAnalyst = macroAnalyst;
        _signal = signal;
        _macroAligner = macroAligner;
        _catalyst = catalyst;
        _thesis = thesis;
        _recommender = recommender;
        _enricher = enricher;
        _portfolio = portfolio;
    }

    public IObservable<StepEvent> Events => _events;

    public async Task<bool> RunAllAsync(string family, string isoWeek, string runId, CancellationToken ct = default)
    {
        _logger.Info("Pipeline run started: family={Family} isoWeek={IsoWeek} runId={RunId}", family, isoWeek, runId);

        foreach (var step in StepId.All)
        {
            ct.ThrowIfCancellationRequested();
            var ok = await RunStepAsync(step, family, isoWeek, runId, ct).ConfigureAwait(false);
            if (!ok)
            {
                _logger.Warn("Pipeline halted at {Step}", step);
                return false;
            }
        }

        _logger.Info("Pipeline run completed: runId={RunId}", runId);
        return true;
    }

    public async Task<bool> RunStepAsync(StepId step, string family, string isoWeek, string runId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _events.OnNext(new StepEvent(step, StepEventKind.Started));

        try
        {
            await InvokeAgentAsync(step, family, isoWeek, runId, ct).ConfigureAwait(false);
            sw.Stop();
            _events.OnNext(new StepEvent(step, StepEventKind.Succeeded, Duration: sw.Elapsed));
            return true;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error(ex, "{Step} failed", step);
            _events.OnNext(new StepEvent(step, StepEventKind.Failed, Message: ex.Message, Duration: sw.Elapsed));
            return false;
        }
    }

    private async Task InvokeAgentAsync(StepId step, string family, string isoWeek, string runId, CancellationToken ct)
    {
        switch (step.Value)
        {
            case 1:  await Task.Run(() => _dataLoader.Run(family, isoWeek, runId), ct).ConfigureAwait(false); break;
            case 2:  await Task.Run(() => _metrics.Run(isoWeek, runId), ct).ConfigureAwait(false); break;
            case 3:  await _macroAnalyst.RunAsync(isoWeek, runId, ct).ConfigureAwait(false); break;
            case 4:  await Task.Run(() => _signal.Run(isoWeek, runId), ct).ConfigureAwait(false); break;
            case 5:  await _macroAligner.RunAsync(isoWeek, runId, ct).ConfigureAwait(false); break;
            case 6:  await _catalyst.RunAsync(isoWeek, runId, ct).ConfigureAwait(false); break;
            case 7:  await _thesis.RunAsync(isoWeek, runId, ct).ConfigureAwait(false); break;
            case 8:  await Task.Run(() => _recommender.Run(isoWeek, runId), ct).ConfigureAwait(false); break;
            case 9:  await _enricher.RunAsync(isoWeek, runId, ct).ConfigureAwait(false); break;
            case 10: await Task.Run(() => _portfolio.Run(isoWeek, runId, null), ct).ConfigureAwait(false); break;
            default: throw new InvalidOperationException($"Unknown step {step}");
        }
    }

    public void Dispose()
    {
        _events.OnCompleted();
        _events.Dispose();
    }
}
