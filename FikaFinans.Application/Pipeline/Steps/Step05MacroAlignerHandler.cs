using System.Diagnostics;

using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Macro;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Default <see cref="IStep05MacroAligner"/>. Reads the fund's record off its progress row and
/// the week's themes out of step 3's context store, and keeps one fund's run in fields, so it
/// must not be shared between funds.
/// </summary>
public sealed class Step05MacroAlignerHandler : IStep05MacroAligner
{
    private readonly ILogger _logger;
    private readonly NavSyncOptions _options;
    private readonly IIsinProgressStore _progress;
    private readonly IMacroContextStore _contexts;
    private readonly IMacroAlignerAgent _agent;
    private readonly IPipelineSignals _signals;
    private readonly IStepEventPublisher _stepEvents;

    /// <summary>
    /// Company filter from configuration, stamped on the output the same way step 1 stamps it.
    /// </summary>
    private Company _family = new(string.Empty);

    /// <summary>Week of the trading date that raised the run.</summary>
    private IsoWeek _isoWeek = IsoWeek.Empty;

    /// <summary>This fund's record as step 4 stored it; empty until it loads.</summary>
    private IReadOnlyList<FundRecord> _funds = Array.Empty<FundRecord>();

    /// <summary>The context whose rotation themes the fund is aligned against; null until it loads.</summary>
    private MacroContext? _context;

    /// <summary>What the agent produced, kept for the phases that persist and emit it.</summary>
    private DataLoaderOutput? _agentOutput;

    /// <summary>This fund's progress row as this run last wrote it; null until it is started.</summary>
    private IsinProgressEntity? _progressRow;

    /// <summary>Runs from the start, so a reported duration covers every phase.</summary>
    private readonly Stopwatch _stepDuration = new();

    public Step05MacroAlignerHandler(
        ILogger logger,
        NavSyncOptions options,
        IIsinProgressStore progress,
        IMacroContextStore contexts,
        IMacroAlignerAgent agent,
        IPipelineSignals signals,
        IStepEventPublisher stepEvents)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(stepEvents);

        _logger = logger;
        _options = options;
        _progress = progress;
        _contexts = contexts;
        _agent = agent;
        _signals = signals;
        _stepEvents = stepEvents;
    }

    /// <inheritdoc />
    public async Task<bool> BeginProcessingAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug(
            "Step 5 begin — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        try
        {
            // A start, not step 1's claim: that one clears the row, and step 4's output on
            // it is what this step is about to read.
            _progressRow = await _progress
                .StartStep05Async(signal.Isin, signal.RunId, ct)
                .ConfigureAwait(false);

            if (_progressRow is null)
            {
                // Delivery is at-least-once, so a signal can outlive its run — the run has
                // ended, or a newer one holds the fund. Either way nothing here is ours.
                _logger.Info(
                    "Step 5 skipped — isin={0}, runId={1} is no longer the fund's run",
                    signal.Isin.Value, signal.RunId.Value);

                return false;
            }

            _logger.Debug("Step 5 started — isin={0}", signal.Isin.Value);

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
                ex, "Step 5 start failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task LoadFundAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 5 load — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _isoWeek = IsoWeek.FromDate(signal.NavDate);

            _family = new Company(_options.CompanyFilter);

            // Step 4's column: every step reads its predecessor's output. The run-wide read is
            // enough — step 1 mints a run id per fund, so the run's output holds this fund's
            // record and nothing else.
            var step04 = await _progress
                .ReadStepOutputAsync(StepId.SignalScorer, signal.RunId, ct)
                .ConfigureAwait(false);

            // The start already proved the run is ours, so an empty column is not a late
            // signal — step 4 stored no record. A failure, so it shows on the row and the
            // next signal retries the fund.
            var record = step04?.Funds.FirstOrDefault(f => f.Isin == signal.Isin)
                ?? throw new InvalidOperationException(
                    $"Step 4 stored no record for ISIN {signal.Isin.Value} in run {signal.RunId.Value}.");

            _funds = [record];

            _context = await LoadContextAsync(_isoWeek, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: without the record and the themes there is nothing to align.
            _logger.Error(ex, "Step 5 load failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public Task AssembleAgentInputAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Nothing to assemble: the record arrives scored and the themes arrive with the
        // context. The phase stays so the entry point matches the other steps', and a config
        // added later has somewhere to load.
        _logger.Debug("Step 5 assemble — isin={0}, nothing to assemble", signal.Isin.Value);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<DataLoaderOutput> RunAgentAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Both are set by the load. Reaching here without them is a caller running the
        // phases out of order, not a condition to recover from.
        if (_funds.Count == 0 || _context is null)
            throw new InvalidOperationException("LoadFundAsync must run before RunAgentAsync.");

        _logger.Debug("Step 5 compute — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            // Each record comes back with its alignment added and every earlier field
            // preserved, so the append-only rule holds here. A failed LLM call is not a
            // failure: the contract defaults the fund to None with a warning.
            _agentOutput = await _agent
                .RunInMemoryAsync(_family, _isoWeek, signal.RunId, _funds, _context.RotationThemes, ct)
                .ConfigureAwait(false);

            // The output's warnings do not reach the row — only the fund's record is stored —
            // so the log is where a fund aligned None by a bad LLM answer shows up.
            foreach (var warning in _agentOutput.DataQuality.Warnings)
                _logger.Warn("Step 5 warning — isin={0}, runId={1}: {2}", signal.Isin.Value, signal.RunId.Value, warning);

            return _agentOutput;
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Step 5 compute failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (_agentOutput is null)
            throw new InvalidOperationException("RunAgentAsync must run before PersistAsync.");

        if (_progressRow is null)
            throw new InvalidOperationException("BeginProcessingAsync must run before PersistAsync.");

        _logger.Debug("Step 5 persist — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .SaveStepOutputAsync(signal.Isin, StepId.MacroAligner, signal.RunId, _agentOutput, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 5 persisted — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
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
            _logger.Error(ex, "Step 5 persist failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task EmitDoneAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 5 emit — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            // Identity only, no payload, same as the earlier steps': a reader takes the record
            // back off the row, so nothing travelling here can disagree with what was stored.
            await _signals
                .PublishAsync(new Step05DoneSignal(signal.Isin, signal.NavDate, signal.RunId), ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 5 emitted — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

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
            _logger.Error(ex, "Step 5 emit failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Completing a row this run never held would free another run's fund.
        if (_progressRow is null)
            throw new InvalidOperationException("BeginProcessingAsync must run before CompleteAsync.");

        _logger.Debug("Step 5 complete — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .CompleteStep05Async(signal.Isin, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 5 completed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — the row stays in flight until it is restarted.
            throw;
        }
        catch (Exception ex)
        {
            // Not reported: the step's work succeeded and EmitDoneAsync already said so.
            // Rethrown so the caller can fail the run instead of leaving the row in flight.
            _logger.Error(
                ex, "Step 5 complete failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task FailAsync(Step04DoneSignal signal, Exception error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(error);

        // No row of ours to free: the start was refused — a newer run holds the fund — or
        // never ran. Failing it anyway would release that other run's lock.
        if (_progressRow is null)
        {
            _logger.Debug("Step 5 fail skipped — isin={0} was never started", signal.Isin.Value);
            return;
        }

        _logger.Debug("Step 5 fail — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .FailStep05Async(signal.Isin, error.Message, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 5 failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
        catch (Exception ex)
        {
            // Swallowed, cancellation included: the caller is already handling the failure
            // that brought it here, and a throw would replace it. The row stays in flight.
            _logger.Error(
                ex, "Step 5 fail could not be recorded — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
    }

    /// <inheritdoc />
    public async Task<int> FailOrphanedRunsAsync(DateTimeOffset startedBefore, CancellationToken ct = default)
    {
        _logger.Debug("Step 5 orphaned-run recovery — startedBefore={0:u}", startedBefore);

        var orphaned = await _progress
            .ListOrphanedStep05Async(startedBefore, ct)
            .ConfigureAwait(false);

        var failed = 0;

        foreach (var row in orphaned)
        {
            try
            {
                // A fail, not a reset: it clears the stale lock and keeps the dedup anchor,
                // so the next NAV sync sees the fund as changed and raises it again.
                await _progress
                    .FailStep05Async(
                        new Isin(row.Isin),
                        "Orphaned run — the process ended before step 5 finished; reprocessed on the next NAV sync.",
                        ct)
                    .ConfigureAwait(false);

                failed++;

                _logger.Info("Step 5 orphaned run failed — isin={0}, runId={1}", row.Isin, row.RunId?.Value);
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
                _logger.Error(ex, "Step 5 orphaned run could not be failed — isin={0}", row.Isin);
            }
        }

        _logger.Debug("Step 5 orphaned-run recovery done — {0} of {1} failed", failed, orphaned.Count);

        return failed;
    }

    /// <inheritdoc />
    public async Task<StepOutput?> ReadOutputAsync(PipelineRunId runId, CancellationToken ct = default)
    {
        _logger.Debug("Step 5 read — runId={0}", runId.Value);

        try
        {
            return await _progress
                .ReadStepOutputAsync(StepId.MacroAligner, runId, ct)
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
            _logger.Error(ex, "Step 5 read failed — runId={0}", runId.Value);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<DataLoaderOutput> ComputeOutputAsync(
        PipelineRunId runId, IsoWeek isoWeek, CancellationToken ct = default)
    {
        _logger.Debug("Step 5 debug compute — runId={0}, week {1}", runId.Value, isoWeek);

        var step04 = await _progress
            .ReadStepOutputAsync(StepId.SignalScorer, runId, ct)
            .ConfigureAwait(false);

        if (step04 is null || step04.Funds.Count == 0)
            throw new InvalidOperationException($"Step 4 stored no records for run {runId.Value}.");

        var context = await LoadContextAsync(isoWeek, ct).ConfigureAwait(false);

        var output = await _agent
            .RunInMemoryAsync(
                new Company(_options.CompanyFilter), isoWeek, runId, step04.Funds, context.RotationThemes, ct)
            .ConfigureAwait(false);

        _logger.Info(
            "Step 5 debug compute done, not stored — runId={0}, week {1}, {2} fund(s), {3} warning(s)",
            runId.Value, isoWeek, output.Funds.Count, output.DataQuality.Warnings.Count);

        return output;
    }

    /// <summary>
    /// The newest context stored up to <paramref name="isoWeek"/> — the same read step 3's tab
    /// makes, so a fund is aligned against what that tab shows for the week.
    /// </summary>
    /// <exception cref="InvalidOperationException">No context is stored up to the week.</exception>
    private async Task<MacroContext> LoadContextAsync(IsoWeek isoWeek, CancellationToken ct)
    {
        // Step 3 fails a fund it has no context for, so a miss here should not arise. A
        // failure, not None: aligning against no themes would look like a real "no theme".
        var context = await _contexts.GetNewestAsync(isoWeek, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No macro context for {isoWeek} or earlier.");

        // An earlier week's context is a normal fallback; it is logged as step 3 logs its own.
        if (!string.Equals(context.IsoWeek, isoWeek.Value, StringComparison.Ordinal))
            _logger.Info("Step 5 context fallback — week {0} aligned against {1}'s context", isoWeek, context.IsoWeek);

        return context;
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
            StepId.MacroAligner,
            kind,
            isin,
            message,
            kind == StepEventKind.Started ? null : _stepDuration.Elapsed));
}
