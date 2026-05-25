using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Sequential orchestrator for the 10 pipeline agents. First Phase 1 slice
/// per <c>Docs/pipeline-step-flow-plan.md</c>: matches today's WPF "Run All"
/// behaviour (universe-wide per-step calls, halt on first failure) but moves
/// the loop out of the WPF VM and emits <see cref="StepEvent"/>s so callers
/// can subscribe. <see cref="RunPerIsinBlockAsync"/> is the per-ISIN
/// streaming primitive — call it with the loaded Step 1 + Step 3 outputs and
/// it streams every fund through Steps 2 → 4 → 5 → 6 → 7 → 8 with
/// <c>Merge(maxConcurrent: N)</c>, emitting per-fund <see cref="StepEvent"/>
/// ticks with <see cref="StepEvent.Isin"/> populated. Wiring it into
/// <see cref="RunAllAsync"/> is the next follow-up slice.
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

    private readonly Subject<StepEvent> _eventsCore = new();
    private readonly object _eventsGate = new();

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

    public IObservable<StepEvent> Events => _eventsCore;

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
        Emit(new StepEvent(step, StepEventKind.Started));

        try
        {
            await InvokeAgentAsync(step, family, isoWeek, runId, ct).ConfigureAwait(false);
            sw.Stop();
            Emit(new StepEvent(step, StepEventKind.Succeeded, Duration: sw.Elapsed));
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
            Emit(new StepEvent(step, StepEventKind.Failed, Message: ex.Message, Duration: sw.Elapsed));
            return false;
        }
    }

    /// <summary>
    /// Per-ISIN block primitive. Streams every fund in <paramref name="step1Output"/>
    /// through Steps 2 → 4 → 5 → 6 → 7 → 8 in memory with
    /// <c>Merge(maxConcurrent: <paramref name="maxConcurrent"/>)</c>. Steps 3
    /// and 9 are universe-wide barriers and must already have been run (Step 3
    /// before this call, Step 9 after); their outputs are not touched here.
    /// Emits per-fund <see cref="StepEvent"/>s with <see cref="StepEvent.Isin"/>
    /// populated on Started/Succeeded for every step; Failed if a step throws.
    /// Returns the enriched universe (all 6 step fields populated). Warnings
    /// from <see cref="FundProcessingResult.Warnings"/> are folded into the
    /// returned <see cref="DataLoaderOutput.DataQuality"/>.
    /// </summary>
    public async Task<DataLoaderOutput> RunPerIsinBlockAsync(
        DataLoaderOutput step1Output,
        MacroContext macroContext,
        MetricsCalculatorConfig metricsConfig,
        SignalScorerConfig signalConfig,
        int maxConcurrent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step1Output);
        ArgumentNullException.ThrowIfNull(macroContext);
        ArgumentNullException.ThrowIfNull(metricsConfig);
        ArgumentNullException.ThrowIfNull(signalConfig);
        if (maxConcurrent < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), maxConcurrent, "maxConcurrent must be at least 1.");

        var activeThemes = macroContext.RotationThemes ?? Array.Empty<RotationTheme>();
        var activeCatalysts = (macroContext.Catalysts ?? Array.Empty<Catalyst>())
            .Where(c => c.AffectedCategories.Count > 0)
            .ToArray();

        var warnings = new ConcurrentBag<string>();

        var enrichedFunds = await step1Output.Funds
            .ToObservable()
            .Select(fund => Observable.FromAsync(token => RunFundAsync(
                fund, metricsConfig, signalConfig, activeThemes, activeCatalysts, warnings, token)))
            .Merge(maxConcurrent)
            .ToList()
            .ToTask(ct)
            .ConfigureAwait(false);

        var mergedWarnings = step1Output.DataQuality.Warnings.Concat(warnings).ToList();

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = step1Output.IsoWeek,
            Family          = step1Output.Family,
            RunId           = step1Output.RunId,
            ConfigVersion   = step1Output.ConfigVersion,
            Funds           = enrichedFunds.ToList(),
            FrozenPositions = step1Output.FrozenPositions,
            CashAvailableKr = step1Output.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = step1Output.DataQuality.MetadataRows,
                SummaryRows   = step1Output.DataQuality.SummaryRows,
                SnapshotRows  = step1Output.DataQuality.SnapshotRows,
                PositionsRows = step1Output.DataQuality.PositionsRows,
                WriteoffCount = step1Output.DataQuality.WriteoffCount,
                CoreCount     = step1Output.DataQuality.CoreCount,
                Warnings      = mergedWarnings,
            },
        };
    }

    private async Task<FundRecord> RunFundAsync(
        FundRecord input,
        MetricsCalculatorConfig metricsConfig,
        SignalScorerConfig signalConfig,
        IReadOnlyList<RotationTheme> activeThemes,
        IReadOnlyList<Catalyst> activeCatalysts,
        ConcurrentBag<string> warnings,
        CancellationToken ct)
    {
        var fund = input;

        fund = RunSyncStep(StepId.MetricsCalculator, fund, f => _metrics.ProcessFund(f, metricsConfig));
        ct.ThrowIfCancellationRequested();

        fund = RunSyncStep(StepId.SignalScorer, fund, f => _signal.ProcessFund(f, signalConfig));
        ct.ThrowIfCancellationRequested();

        fund = await RunAsyncStep(StepId.MacroAligner, fund, warnings,
            (f, token) => _macroAligner.ProcessFundAsync(f, activeThemes, token), ct).ConfigureAwait(false);

        fund = await RunAsyncStep(StepId.CatalystTagger, fund, warnings,
            (f, token) => _catalyst.ProcessFundAsync(f, activeCatalysts, token), ct).ConfigureAwait(false);

        fund = await RunAsyncStep(StepId.ThesisValidator, fund, warnings,
            (f, token) => _thesis.ProcessFundAsync(f, token), ct).ConfigureAwait(false);

        fund = RunSyncResultStep(StepId.Recommender, fund, warnings, f => _recommender.ProcessFund(f));

        return fund;
    }

    private FundRecord RunSyncStep(StepId step, FundRecord input, Func<FundRecord, FundRecord> body)
    {
        Emit(new StepEvent(step, StepEventKind.Started, Isin: input.Isin));
        try
        {
            var result = body(input);
            Emit(new StepEvent(step, StepEventKind.Succeeded, Isin: result.Isin));
            return result;
        }
        catch (Exception ex)
        {
            Emit(new StepEvent(step, StepEventKind.Failed, Isin: input.Isin, Message: ex.Message));
            throw;
        }
    }

    private FundRecord RunSyncResultStep(
        StepId step,
        FundRecord input,
        ConcurrentBag<string> warnings,
        Func<FundRecord, FundProcessingResult> body)
    {
        Emit(new StepEvent(step, StepEventKind.Started, Isin: input.Isin));
        try
        {
            var result = body(input);
            foreach (var w in result.Warnings) warnings.Add(w);
            Emit(new StepEvent(step, StepEventKind.Succeeded, Isin: result.Fund.Isin));
            return result.Fund;
        }
        catch (Exception ex)
        {
            Emit(new StepEvent(step, StepEventKind.Failed, Isin: input.Isin, Message: ex.Message));
            throw;
        }
    }

    private async Task<FundRecord> RunAsyncStep(
        StepId step,
        FundRecord input,
        ConcurrentBag<string> warnings,
        Func<FundRecord, CancellationToken, Task<FundProcessingResult>> body,
        CancellationToken ct)
    {
        Emit(new StepEvent(step, StepEventKind.Started, Isin: input.Isin));
        try
        {
            var result = await body(input, ct).ConfigureAwait(false);
            foreach (var w in result.Warnings) warnings.Add(w);
            Emit(new StepEvent(step, StepEventKind.Succeeded, Isin: result.Fund.Isin));
            return result.Fund;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Emit(new StepEvent(step, StepEventKind.Failed, Isin: input.Isin, Message: ex.Message));
            throw;
        }
    }

    // Subject<T>.OnNext is not thread-safe. The per-ISIN block fans out under
    // Merge(maxConcurrent: N), so concurrent emissions need serialisation.
    private void Emit(StepEvent evt)
    {
        lock (_eventsGate)
        {
            _eventsCore.OnNext(evt);
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
        _eventsCore.OnCompleted();
        _eventsCore.Dispose();
    }
}
