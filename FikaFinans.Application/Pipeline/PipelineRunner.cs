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
    private readonly IStreamingPipelineGateway _gateway;

    private readonly Subject<StepEvent> _eventsCore = new();
    private readonly object _eventsGate = new();

    private static readonly IReadOnlyList<StepId> PerIsinSteps =
    [
        StepId.MetricsCalculator, StepId.SignalScorer, StepId.MacroAligner,
        StepId.CatalystTagger,    StepId.ThesisValidator, StepId.Recommender,
    ];

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
        IPortfolioConstructorAgent portfolio,
        IStreamingPipelineGateway gateway)
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
        ArgumentNullException.ThrowIfNull(gateway);

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
        _gateway = gateway;
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

    public async Task<bool> RunAllStreamingAsync(
        string family,
        string isoWeek,
        string runId,
        int maxConcurrent = 5,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        _logger.Info(
            "Streaming pipeline run started: family={Family} isoWeek={IsoWeek} runId={RunId} maxConcurrent={MaxConcurrent}",
            family, isoWeek, runId, maxConcurrent);

        // Universe-wide barriers before the per-ISIN block.
        if (!await RunStepAsync(StepId.DataLoader, family, isoWeek, runId, ct).ConfigureAwait(false))
        {
            _logger.Warn("Streaming pipeline halted at {Step}", StepId.DataLoader);
            return false;
        }

        if (!await RunStepAsync(StepId.MacroAnalyst, family, isoWeek, runId, ct).ConfigureAwait(false))
        {
            _logger.Warn("Streaming pipeline halted at {Step}", StepId.MacroAnalyst);
            return false;
        }

        // Per-ISIN block (Steps 2 → 4 → 5 → 6 → 7 → 8). Universe-wide
        // Started events fire for all six steps at block start; per-fund
        // Started/Succeeded events with Isin populated stream during
        // execution; universe-wide Succeeded events for all six steps fire
        // once the boundary files are written.
        DataLoaderOutput step1Output;
        MacroContext macroContext;
        MetricsCalculatorConfig metricsConfig;
        SignalScorerConfig signalConfig;
        try
        {
            step1Output = _gateway.LoadStep1Output(isoWeek, runId);
            macroContext = _gateway.LoadStep3Output(isoWeek, runId);
            metricsConfig = _gateway.LoadMetricsConfig();
            signalConfig = _gateway.LoadSignalConfig();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load streaming pipeline inputs from gateway");
            foreach (var step in PerIsinSteps)
                Emit(new StepEvent(step, StepEventKind.Failed, Message: ex.Message));
            return false;
        }

        var totalFunds = step1Output.Funds.Count;
        var blockSw = Stopwatch.StartNew();
        foreach (var step in PerIsinSteps)
            Emit(new StepEvent(step, StepEventKind.Started, Total: totalFunds));

        try
        {
            var result = await RunPerIsinBlockAsync(
                step1Output, macroContext, metricsConfig, signalConfig, maxConcurrent, ct)
                .ConfigureAwait(false);

            _gateway.SaveStepOutput(StepId.MetricsCalculator, isoWeek, runId, result.Step2Output);
            _gateway.SaveStepOutput(StepId.SignalScorer,      isoWeek, runId, result.Step4Output);
            _gateway.SaveStepOutput(StepId.MacroAligner,      isoWeek, runId, result.Step5Output);
            _gateway.SaveStepOutput(StepId.CatalystTagger,    isoWeek, runId, result.Step6Output);
            _gateway.SaveStepOutput(StepId.ThesisValidator,   isoWeek, runId, result.Step7Output);
            _gateway.SaveStepOutput(StepId.Recommender,       isoWeek, runId, result.Step8Output);
        }
        catch (OperationCanceledException)
        {
            blockSw.Stop();
            throw;
        }
        catch (Exception ex)
        {
            blockSw.Stop();
            _logger.Error(ex, "Streaming per-ISIN block failed");
            foreach (var step in PerIsinSteps)
                Emit(new StepEvent(step, StepEventKind.Failed, Message: ex.Message, Duration: blockSw.Elapsed));
            return false;
        }

        blockSw.Stop();
        foreach (var step in PerIsinSteps)
            Emit(new StepEvent(step, StepEventKind.Succeeded, Duration: blockSw.Elapsed));

        // Universe-wide barriers after the per-ISIN block.
        if (!await RunStepAsync(StepId.UniverseEnricher, family, isoWeek, runId, ct).ConfigureAwait(false))
        {
            _logger.Warn("Streaming pipeline halted at {Step}", StepId.UniverseEnricher);
            return false;
        }

        if (!await RunStepAsync(StepId.PortfolioConstructor, family, isoWeek, runId, ct).ConfigureAwait(false))
        {
            _logger.Warn("Streaming pipeline halted at {Step}", StepId.PortfolioConstructor);
            return false;
        }

        _logger.Info("Streaming pipeline run completed: runId={RunId}", runId);
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
    /// Returns six <see cref="DataLoaderOutput"/> snapshots — one per
    /// per-ISIN step boundary — so callers can persist matching JSON files.
    /// Each snapshot preserves the input fund order and folds the per-fund
    /// warnings from Steps 5–8 back into <see cref="DataQuality.Warnings"/>.
    /// </summary>
    public async Task<PerIsinBlockResult> RunPerIsinBlockAsync(
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
        var captures = new PerStepCaptures();

        await step1Output.Funds
            .ToObservable()
            .Select(fund => Observable.FromAsync(token => RunFundAsync(
                fund, metricsConfig, signalConfig, activeThemes, activeCatalysts, warnings, captures, token)))
            .Merge(maxConcurrent)
            .ToList()
            .ToTask(ct)
            .ConfigureAwait(false);

        var mergedWarnings = step1Output.DataQuality.Warnings.Concat(warnings).ToList();

        return new PerIsinBlockResult(
            Step2Output: BuildUniverse(step1Output, captures.Step2, mergedWarnings),
            Step4Output: BuildUniverse(step1Output, captures.Step4, mergedWarnings),
            Step5Output: BuildUniverse(step1Output, captures.Step5, mergedWarnings),
            Step6Output: BuildUniverse(step1Output, captures.Step6, mergedWarnings),
            Step7Output: BuildUniverse(step1Output, captures.Step7, mergedWarnings),
            Step8Output: BuildUniverse(step1Output, captures.Step8, mergedWarnings));
    }

    private static DataLoaderOutput BuildUniverse(
        DataLoaderOutput template,
        ConcurrentDictionary<string, FundRecord> capture,
        List<string> warnings)
    {
        var ordered = template.Funds
            .Select(f => capture[f.Isin.Value])
            .ToList();

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = template.IsoWeek,
            Family          = template.Family,
            RunId           = template.RunId,
            ConfigVersion   = template.ConfigVersion,
            Funds           = ordered,
            FrozenPositions = template.FrozenPositions,
            CashAvailableKr = template.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = template.DataQuality.MetadataRows,
                SummaryRows   = template.DataQuality.SummaryRows,
                SnapshotRows  = template.DataQuality.SnapshotRows,
                PositionsRows = template.DataQuality.PositionsRows,
                WriteoffCount = template.DataQuality.WriteoffCount,
                CoreCount     = template.DataQuality.CoreCount,
                Warnings      = warnings,
            },
        };
    }

    // Per-step per-fund snapshots collected during the Merge fan-out. Each
    // dictionary is keyed by Isin.Value (a plain string for thread-safe
    // hashing) so we can reassemble in input order via the step1 template.
    private sealed class PerStepCaptures
    {
        public ConcurrentDictionary<string, FundRecord> Step2 { get; } = new();
        public ConcurrentDictionary<string, FundRecord> Step4 { get; } = new();
        public ConcurrentDictionary<string, FundRecord> Step5 { get; } = new();
        public ConcurrentDictionary<string, FundRecord> Step6 { get; } = new();
        public ConcurrentDictionary<string, FundRecord> Step7 { get; } = new();
        public ConcurrentDictionary<string, FundRecord> Step8 { get; } = new();
    }

    private async Task<FundRecord> RunFundAsync(
        FundRecord input,
        MetricsCalculatorConfig metricsConfig,
        SignalScorerConfig signalConfig,
        IReadOnlyList<RotationTheme> activeThemes,
        IReadOnlyList<Catalyst> activeCatalysts,
        ConcurrentBag<string> warnings,
        PerStepCaptures captures,
        CancellationToken ct)
    {
        var fund = input;

        fund = RunSyncStep(StepId.MetricsCalculator, fund, f => _metrics.ProcessFund(f, metricsConfig));
        captures.Step2[fund.Isin.Value] = fund;
        ct.ThrowIfCancellationRequested();

        fund = RunSyncStep(StepId.SignalScorer, fund, f => _signal.ProcessFund(f, signalConfig));
        captures.Step4[fund.Isin.Value] = fund;
        ct.ThrowIfCancellationRequested();

        fund = await RunAsyncStep(StepId.MacroAligner, fund, warnings,
            (f, token) => _macroAligner.ProcessFundAsync(f, activeThemes, token), ct).ConfigureAwait(false);
        captures.Step5[fund.Isin.Value] = fund;

        fund = await RunAsyncStep(StepId.CatalystTagger, fund, warnings,
            (f, token) => _catalyst.ProcessFundAsync(f, activeCatalysts, token), ct).ConfigureAwait(false);
        captures.Step6[fund.Isin.Value] = fund;

        fund = await RunAsyncStep(StepId.ThesisValidator, fund, warnings,
            (f, token) => _thesis.ProcessFundAsync(f, token), ct).ConfigureAwait(false);
        captures.Step7[fund.Isin.Value] = fund;

        fund = RunSyncResultStep(StepId.Recommender, fund, warnings, f => _recommender.ProcessFund(f));
        captures.Step8[fund.Isin.Value] = fund;

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
