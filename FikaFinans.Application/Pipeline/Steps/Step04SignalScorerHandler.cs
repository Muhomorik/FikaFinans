using System.Diagnostics;

using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Default <see cref="IStep04SignalScorer"/>. Everything it reads is already on the fund's
/// progress row or behind the config provider, and it keeps one fund's run in fields, so it
/// must not be shared between funds.
/// </summary>
public sealed class Step04SignalScorerHandler : IStep04SignalScorer
{
    private readonly ILogger _logger;
    private readonly NavSyncOptions _options;
    private readonly IIsinProgressStore _progress;
    private readonly IStep04SignalsConfigProvider _configProvider;
    private readonly ISignalScorerAgent _agent;
    private readonly IPipelineSignals _signals;
    private readonly IStepEventPublisher _stepEvents;

    /// <summary>
    /// Company filter from configuration, stamped on the output the same way step 1 stamps it.
    /// </summary>
    private Company _family = new(string.Empty);

    /// <summary>Week of the trading date that raised the run.</summary>
    private IsoWeek _isoWeek = IsoWeek.Empty;

    /// <summary>This fund's record as step 3 stored it; empty until it loads.</summary>
    private IReadOnlyList<FundRecord> _funds = Array.Empty<FundRecord>();

    /// <summary>The settings the agent runs with; null until assembled.</summary>
    private SignalScorerConfig? _config;

    /// <summary>What the agent produced, kept for the phases that persist and emit it.</summary>
    private DataLoaderOutput? _agentOutput;

    /// <summary>This fund's progress row as this run last wrote it; null until it is started.</summary>
    private IsinProgressEntity? _progressRow;

    /// <summary>Runs from the start, so a reported duration covers every phase.</summary>
    private readonly Stopwatch _stepDuration = new();

    public Step04SignalScorerHandler(
        ILogger logger,
        NavSyncOptions options,
        IIsinProgressStore progress,
        IStep04SignalsConfigProvider configProvider,
        ISignalScorerAgent agent,
        IPipelineSignals signals,
        IStepEventPublisher stepEvents)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(configProvider);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(stepEvents);

        _logger = logger;
        _options = options;
        _progress = progress;
        _configProvider = configProvider;
        _agent = agent;
        _signals = signals;
        _stepEvents = stepEvents;
    }

    /// <inheritdoc />
    public async Task<bool> BeginProcessingAsync(Step03DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug(
            "Step 4 begin — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        try
        {
            // A start, not step 1's claim: that one clears the row, and step 3's output on
            // it is what this step is about to read.
            _progressRow = await _progress
                .StartStep04Async(signal.Isin, signal.RunId, ct)
                .ConfigureAwait(false);

            if (_progressRow is null)
            {
                // Delivery is at-least-once, so a signal can outlive its run — the run has
                // ended, or a newer one holds the fund. Either way nothing here is ours.
                _logger.Info(
                    "Step 4 skipped — isin={0}, runId={1} is no longer the fund's run",
                    signal.Isin.Value, signal.RunId.Value);

                return false;
            }

            _logger.Debug("Step 4 started — isin={0}", signal.Isin.Value);

            _stepDuration.Restart();
            Report(StepEventKind.Started, signal.Isin);

            return true;
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex, "Step 4 start failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task LoadFundAsync(Step03DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 4 load — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _isoWeek = IsoWeek.FromDate(signal.NavDate);

            _family = new Company(_options.CompanyFilter);

            // Step 3's column, not step 2's: every step reads its predecessor's output. The
            // run-wide read is enough — step 1 mints a run id per fund, so the run's output
            // holds this fund's record and nothing else.
            var step03 = await _progress
                .ReadStepOutputAsync(StepId.MacroAnalyst, signal.RunId, ct)
                .ConfigureAwait(false);

            // The start already proved the run is ours, so an empty column is not a late
            // signal — step 3 stored no record. A failure, so it shows on the row and the
            // next signal retries the fund.
            var record = step03?.Funds.FirstOrDefault(f => f.Isin == signal.Isin)
                ?? throw new InvalidOperationException(
                    $"Step 3 stored no record for ISIN {signal.Isin.Value} in run {signal.RunId.Value}.");

            _funds = [record];
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: without the record there is nothing for the agent to score.
            _logger.Error(ex, "Step 4 load failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task AssembleAgentInputAsync(Step03DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 4 assemble — isin={0}", signal.Isin.Value);

        try
        {
            // The record arrives assembled — step 2 computed the metrics, step 3 passed them
            // through. Only the config is left, and the provider decides what a missing one
            // means.
            _config = await _configProvider.GetConfigAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: running on settings the user did not choose would look like a
            // normal result and be wrong.
            _logger.Error(ex, "Step 4 assemble failed — isin={0}", signal.Isin.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public Task<DataLoaderOutput> RunAgentAsync(Step03DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Both are set by the phases this one runs after. Reaching here without them is a
        // caller running the phases out of order, not a condition to recover from.
        if (_funds.Count == 0)
            throw new InvalidOperationException("LoadFundAsync must run before RunAgentAsync.");

        if (_config is null)
            throw new InvalidOperationException("AssembleAgentInputAsync must run before RunAgentAsync.");

        _logger.Debug("Step 4 compute — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            // Everything the agent scores from is already in memory by now — this phase
            // opens no file and touches no database. Each record comes back with its signal
            // added and every earlier field preserved, so the append-only rule holds here.
            _agentOutput = _agent.RunInMemory(_family, _isoWeek, signal.RunId, _funds, _config);

            return Task.FromResult(_agentOutput);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Step 4 compute failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistAsync(Step03DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (_agentOutput is null)
            throw new InvalidOperationException("RunAgentAsync must run before PersistAsync.");

        if (_progressRow is null)
            throw new InvalidOperationException("BeginProcessingAsync must run before PersistAsync.");

        _logger.Debug("Step 4 persist — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .SaveStepOutputAsync(signal.Isin, StepId.SignalScorer, signal.RunId, _agentOutput, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 4 persisted — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: EmitDoneAsync must not announce an output that was never stored.
            // The caller fails the run, which frees the row.
            _logger.Error(ex, "Step 4 persist failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task EmitDoneAsync(Step03DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 4 emit — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            // Identity only, no payload, same as the earlier steps': a reader takes the record
            // back off the row, so nothing travelling here can disagree with what was stored.
            await _signals
                .PublishAsync(new Step04DoneSignal(signal.Isin, signal.NavDate, signal.RunId), ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 4 emitted — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            // The last phase of the step's own work, so the step is done for this fund.
            Report(StepEventKind.Succeeded, signal.Isin);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // The output is stored, but nobody was told. Rethrown so the caller can fail
            // the run — the anchor stays put, so the next signal redoes the fund.
            _logger.Error(ex, "Step 4 emit failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task FailAsync(Step03DoneSignal signal, Exception error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(error);

        // No row of ours to free: the start was refused — a newer run holds the fund — or
        // never ran. Failing it anyway would release that other run's lock.
        if (_progressRow is null)
        {
            _logger.Debug("Step 4 fail skipped — isin={0} was never started", signal.Isin.Value);
            return;
        }

        _logger.Debug("Step 4 fail — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .FailStep04Async(signal.Isin, error.Message, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 4 failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
        catch (Exception ex)
        {
            // Swallowed, cancellation included: the caller is already handling the failure
            // that brought it here, and a throw would replace it. The row stays in flight.
            _logger.Error(
                ex, "Step 4 fail could not be recorded — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
    }

    /// <inheritdoc />
    public async Task<int> FailOrphanedRunsAsync(DateTimeOffset startedBefore, CancellationToken ct = default)
    {
        _logger.Debug("Step 4 orphaned-run recovery — startedBefore={0:u}", startedBefore);

        var orphaned = await _progress
            .ListOrphanedStep04Async(startedBefore, ct)
            .ConfigureAwait(false);

        var failed = 0;

        foreach (var row in orphaned)
        {
            try
            {
                // A fail, not a reset: it clears the stale lock and keeps the dedup anchor,
                // so the next NAV sync sees the fund as changed and raises it again.
                await _progress
                    .FailStep04Async(
                        new Isin(row.Isin),
                        "Orphaned run — the process ended before step 4 finished; reprocessed on the next NAV sync.",
                        ct)
                    .ConfigureAwait(false);

                failed++;

                _logger.Info("Step 4 orphaned run failed — isin={0}, runId={1}", row.Isin, row.RunId?.Value);
            }
            catch (OperationCanceledException)
            {
                // Shutdown, not a failure — the rest stay locked until the next recovery.
                throw;
            }
            catch (Exception ex)
            {
                // Logged and skipped: one row the store will not write must not leave every
                // other orphaned run locked too.
                _logger.Error(ex, "Step 4 orphaned run could not be failed — isin={0}", row.Isin);
            }
        }

        _logger.Debug("Step 4 orphaned-run recovery done — {0} of {1} failed", failed, orphaned.Count);

        return failed;
    }

    /// <inheritdoc />
    public async Task<StepOutput?> ReadOutputAsync(PipelineRunId runId, CancellationToken ct = default)
    {
        _logger.Debug("Step 4 read — runId={0}", runId.Value);

        try
        {
            return await _progress
                .ReadStepOutputAsync(StepId.SignalScorer, runId, ct)
                .ConfigureAwait(false);
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
            _logger.Error(ex, "Step 4 read failed — runId={0}", runId.Value);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<DataLoaderOutput> ComputeOutputAsync(
        PipelineRunId runId, IsoWeek isoWeek, CancellationToken ct = default)
    {
        _logger.Debug("Step 4 debug compute — runId={0}, week {1}", runId.Value, isoWeek);

        var step03 = await _progress
            .ReadStepOutputAsync(StepId.MacroAnalyst, runId, ct)
            .ConfigureAwait(false);

        if (step03 is null || step03.Funds.Count == 0)
            throw new InvalidOperationException($"Step 3 stored no records for run {runId.Value}.");

        var config = await _configProvider.GetConfigAsync(ct).ConfigureAwait(false);

        var output = _agent.RunInMemory(
            new Company(_options.CompanyFilter), isoWeek, runId, step03.Funds, config);

        _logger.Info(
            "Step 4 debug compute done, not stored — runId={0}, week {1}, {2} fund(s)",
            runId.Value, isoWeek, output.Funds.Count);

        return output;
    }

    /// <summary>
    /// Reports this step's progress for one fund. Elapsed time is measured from the start,
    /// so it spans every phase rather than the one that happened to report.
    /// </summary>
    /// <param name="kind">Started carries no duration; there is nothing elapsed yet.</param>
    /// <param name="isin">The fund the tick is about.</param>
    /// <param name="message">Why it failed, for a failure; otherwise null.</param>
    private void Report(StepEventKind kind, Isin isin, string? message = null)
        => _stepEvents.Publish(new StepEvent(
            StepId.SignalScorer,
            kind,
            isin,
            message,
            kind == StepEventKind.Started ? null : _stepDuration.Elapsed));
}
