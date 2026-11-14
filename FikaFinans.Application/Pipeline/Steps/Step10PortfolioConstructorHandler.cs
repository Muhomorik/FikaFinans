using System.Diagnostics;

using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Default <see cref="IStep10PortfolioConstructor"/>. Reads step 9's records off every fund's
/// row, lays the portfolio over them and keeps one run in fields, so it must not be shared
/// between runs.
/// </summary>
/// <remarks>
/// The macro regime is passed as null: whether to read it, and how the enum maps onto the
/// override table's keys, is open on step 10's page — and the override is off by default.
/// </remarks>
public sealed class Step10PortfolioConstructorHandler : IStep10PortfolioConstructor
{
    /// <summary>The step event's message when a run finds nothing to trade on.</summary>
    internal const string NoRecordsMessage = "No fund has a step 9 record — nothing to trade";

    private readonly ILogger _logger;
    private readonly IStep09Barrier _barrier;
    private readonly IIsinProgressStore _progress;
    private readonly IStep10PortfolioConfigProvider _configProvider;
    private readonly IStep10PortfolioReader _portfolioReader;
    private readonly IPortfolioConstructorAgent _agent;
    private readonly IPipelineRunIdFactory _runIds;
    private readonly TimeProvider _clock;
    private readonly IStepEventPublisher _stepEvents;
    private readonly IPipelineSignals _signals;

    /// <summary>The run's own id — no fund's run; empty until the run begins.</summary>
    private PipelineRunId _runId = new(string.Empty);

    /// <summary>The week the run happens in — what the output is stamped with.</summary>
    private IsoWeek _isoWeek = IsoWeek.Empty;

    /// <summary>Step 9's records; null until they load.</summary>
    private IReadOnlyList<FundRecord>? _records;

    /// <summary>The settings this run uses; null until assembled.</summary>
    private PortfolioConstructorConfig? _config;

    /// <summary>The records with the holdings laid over them; null until assembled.</summary>
    private Step10Portfolio? _portfolio;

    /// <summary>What the agent produced, kept for the phase that persists it.</summary>
    private TradesOutput? _agentOutput;

    /// <summary>Set once the trades are stored — the done-signal may not go out before.</summary>
    private bool _persisted;

    /// <summary>Runs from the start, so a reported duration covers every phase.</summary>
    private readonly Stopwatch _stepDuration = new();

    public Step10PortfolioConstructorHandler(
        ILogger logger,
        IStep09Barrier barrier,
        IIsinProgressStore progress,
        IStep10PortfolioConfigProvider configProvider,
        IStep10PortfolioReader portfolioReader,
        IPortfolioConstructorAgent agent,
        IPipelineRunIdFactory runIds,
        TimeProvider clock,
        IStepEventPublisher stepEvents,
        IPipelineSignals signals)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(barrier);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(configProvider);
        ArgumentNullException.ThrowIfNull(portfolioReader);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(runIds);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(stepEvents);
        ArgumentNullException.ThrowIfNull(signals);

        _logger = logger;
        _barrier = barrier;
        _progress = progress;
        _configProvider = configProvider;
        _portfolioReader = portfolioReader;
        _agent = agent;
        _runIds = runIds;
        _clock = clock;
        _stepEvents = stepEvents;
        _signals = signals;
    }

    /// <inheritdoc />
    public Task<PipelineRunId> BeginRunAsync(CancellationToken ct = default)
    {
        // No trading date behind a run, so the clock decides both — step 9's pattern.
        var now = _clock.GetUtcNow();
        _isoWeek = IsoWeek.FromDate(now);
        _runId = _runIds.NewUniverseRunId(now);

        _logger.Debug("Step 10 begin — runId={0}, week {1}", _runId.Value, _isoWeek);

        _stepDuration.Restart();
        Report(StepEventKind.Started);

        return Task.FromResult(_runId);
    }

    /// <inheritdoc />
    public async Task<int> LoadUniverseAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_runId.Value))
            throw new InvalidOperationException("BeginRunAsync must run before LoadUniverseAsync.");

        _logger.Debug("Step 10 load — runId={0}", _runId.Value);

        try
        {
            _records = await ReadStep09RecordsAsync(ct).ConfigureAwait(false);

            // Nothing to trade on — no step 9 pass stored yet. The caller stops here, so this
            // closes the run that BeginRunAsync reported started, rather than leaving it open.
            if (_records.Count == 0)
            {
                _stepDuration.Stop();
                Report(StepEventKind.Succeeded, NoRecordsMessage);
            }

            return _records.Count;
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: without the records there is nothing to trade on.
            _logger.Error(ex, "Step 10 load failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task AssembleAgentInputAsync(CancellationToken ct = default)
    {
        if (_records is null)
            throw new InvalidOperationException("LoadUniverseAsync must run before AssembleAgentInputAsync.");

        _logger.Debug("Step 10 assemble — runId={0}", _runId.Value);

        try
        {
            _config = await _configProvider.GetConfigAsync(ct).ConfigureAwait(false);
            _portfolio = await _portfolioReader.ReadAsync(_records, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: trades sized on a portfolio or limits that are not the real ones would
            // look like a normal result and be wrong.
            _logger.Error(ex, "Step 10 assemble failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public Task<TradesOutput> RunAgentAsync(CancellationToken ct = default)
    {
        // Both are set by the phase this one runs after. Reaching here without them is a
        // caller running the phases out of order, not a condition to recover from.
        if (_portfolio is null || _config is null)
            throw new InvalidOperationException("AssembleAgentInputAsync must run before RunAgentAsync.");

        _logger.Debug("Step 10 compute — runId={0}, week {1}, {2} fund(s)",
            _runId.Value, _isoWeek, _portfolio.Funds.Count);

        try
        {
            _agentOutput = Construct(_isoWeek, _portfolio, _config);

            LogTrades(_runId, _agentOutput);

            return Task.FromResult(_agentOutput);
        }
        catch (Exception ex)
        {
            // Config drift and a held fund with a broken value land here: the agent halts on both.
            _logger.Error(ex, "Step 10 compute failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistAsync(CancellationToken ct = default)
    {
        if (_agentOutput is null || _portfolio is null)
            throw new InvalidOperationException("RunAgentAsync must run before PersistAsync.");

        _logger.Debug("Step 10 persist — runId={0}, {1} trade(s)", _runId.Value, _agentOutput.Trades.Count);

        try
        {
            var written = await _progress.SaveStep10Async(_runId, _agentOutput, ct).ConfigureAwait(false);

            _stepDuration.Stop();

            var summary = _agentOutput.CapitalSummary;
            _logger.Info(
                "Step 10 run done — runId={0}, week {1}, funds {2}, trades {3}, rejected {4}, violations {5}, " +
                "deployable {6} kr, bought {7} kr, cash left {8} kr, held without record {9}, rows written {10}, {11} ms",
                _runId.Value, _isoWeek, _portfolio.Funds.Count, _agentOutput.Trades.Count,
                _agentOutput.RejectedRecommendations.Count, _agentOutput.ConstraintViolations.Count,
                summary.TotalDeployableKr, summary.TotalBuyAmountKr, summary.CashRemainingKr,
                _portfolio.HeldWithoutRecord.Count, written.Count, _stepDuration.ElapsedMilliseconds);

            // The emit that follows is a hand-off, not part of the step: the trades are
            // stored, so the step is done here.
            _persisted = true;
            Report(StepEventKind.Succeeded);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report. Rows already written keep this
            // run's slices; the next run overwrites them.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: the caller logs the run as failed. Nothing to undo — the next run
            // overwrites what landed.
            _logger.Error(ex, "Step 10 persist failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task EmitDoneAsync(CancellationToken ct = default)
    {
        if (!_persisted)
            throw new InvalidOperationException("PersistAsync must succeed before EmitDoneAsync.");

        _logger.Debug("Step 10 emit — runId={0}", _runId.Value);

        try
        {
            // Run id only, no payload, as every done-signal: the bank side reads the trades
            // back through the store, so nothing travelling here can disagree with them.
            await _signals.PublishAsync(new Step10DoneSignal(_runId), ct).ConfigureAwait(false);

            _logger.Debug("Step 10 emitted — runId={0}", _runId.Value);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // The trades are stored and the step reported succeeded; only the hand-off is
            // lost, and the next run emits again. Rethrown so the caller sees it.
            _logger.Error(ex, "Step 10 emit failed, trades already stored — runId={0}", _runId.Value);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TradesOutput?> ReadOutputAsync(CancellationToken ct = default)
    {
        _logger.Debug("Step 10 read");

        try
        {
            return await _progress.ReadStep10OutputAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // A read feeds a view, so a failure costs a display rather than a run. Null lets
            // the caller show its "nothing here" state instead of surfacing a crash.
            _logger.Error(ex, "Step 10 read failed");

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TradesOutput> ComputeOutputAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var isoWeek = IsoWeek.FromDate(now);
        var runId = _runIds.NewUniverseRunId(now);

        _logger.Debug("Step 10 debug compute — runId={0}, week {1}", runId.Value, isoWeek);

        var records = await ReadStep09RecordsAsync(ct).ConfigureAwait(false);
        if (records.Count == 0)
            throw new InvalidOperationException("No fund in the universe has a step 9 record.");

        var config = await _configProvider.GetConfigAsync(ct).ConfigureAwait(false);
        var portfolio = await _portfolioReader.ReadAsync(records, ct).ConfigureAwait(false);

        var output = Construct(isoWeek, portfolio, config);

        LogTrades(runId, output);

        _logger.Info(
            "Step 10 debug compute done, not stored — runId={0}, week {1}, {2} fund(s), {3} trade(s), {4} violation(s)",
            runId.Value, isoWeek, portfolio.Funds.Count, output.Trades.Count, output.ConstraintViolations.Count);

        return output;
    }

    /// <summary>
    /// Step 9's records for step 9's universe — the company-filtered funds. The barrier is
    /// asked for its universe only; whether it has closed is step 9's question, not this one.
    /// </summary>
    private async Task<IReadOnlyList<FundRecord>> ReadStep09RecordsAsync(CancellationToken ct)
    {
        var barrier = await _barrier.CheckAsync(ct).ConfigureAwait(false);
        var output = await _progress.ReadStep09OutputAsync(barrier.Universe, ct).ConfigureAwait(false);
        var records = output?.Funds ?? Array.Empty<FundRecord>();

        var loaded = records.Select(f => f.Isin).ToHashSet();
        var leftOut = barrier.Universe.Where(i => !loaded.Contains(i)).Select(i => i.Value).ToList();

        // Info, not Debug: who the run traded without is the first thing to look for when a
        // fund is missing from the day's trades.
        _logger.Info("Step 10 loaded — {0} of {1} fund(s) with a step 9 record, left out: [{2}]",
            records.Count, barrier.Universe.Count, string.Join(", ", leftOut));

        return records;
    }

    /// <summary>The agent call both the run and the debug run make.</summary>
    private TradesOutput Construct(IsoWeek isoWeek, Step10Portfolio portfolio, PortfolioConstructorConfig config)
        => _agent.RunInMemory(
            isoWeek, portfolio.Funds, portfolio.FrozenPositions, portfolio.CashAvailableKr, config,
            macroRegime: null);

    /// <summary>
    /// One line per trade and per rejection — what the bank side will act on, and why the
    /// rest was not — and one Warn per constraint violation.
    /// </summary>
    private void LogTrades(PipelineRunId runId, TradesOutput output)
    {
        foreach (var trade in output.Trades)
            _logger.Debug(
                "Step 10 trade — isin={0}: {1} {2} kr, {3}, conviction {4}, pair {5}",
                trade.Isin.Value, trade.TradeType, trade.AmountKr, trade.TradeReason,
                trade.SourceConviction, trade.RotationPairId ?? "-");

        foreach (var rejected in output.RejectedRecommendations)
            _logger.Debug(
                "Step 10 rejected — isin={0}: {1}, {2}, would have been {3}",
                rejected.Isin.Value, rejected.SourceRecommendation, rejected.RejectedBecause, rejected.WouldHaveBeen);

        foreach (var violation in output.ConstraintViolations)
            _logger.Warn(
                "Step 10 violation — runId={0}, {1}, isin={2}: {3}",
                runId.Value, violation.ViolationType, violation.Isin?.Value ?? "-", violation.Message);
    }

    /// <summary>
    /// Reports this step's progress for the run — no fund, as the runner reports step 10.
    /// Elapsed time is measured from the start, so it spans every phase.
    /// </summary>
    /// <param name="kind">Started carries no duration; there is nothing elapsed yet.</param>
    /// <param name="message">Why it failed, for a failure; otherwise null.</param>
    private void Report(StepEventKind kind, string? message = null)
        => _stepEvents.Publish(new StepEvent(
            StepId.PortfolioConstructor,
            kind,
            Message: message,
            Duration: kind == StepEventKind.Started ? null : _stepDuration.Elapsed));
}
