using System.Diagnostics;

using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Analytics;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Fetch;
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
/// Default <see cref="IStep03MacroAnalyst"/>. A per-fund hop doing per-week work: every fund
/// of a week runs with the same macro context, computed by the LLM once and read from the
/// context store after that. Keeps one fund's run in fields, so it must not be shared
/// between funds — the single-flight it is given must be.
/// </summary>
public sealed class Step03MacroAnalystHandler : IStep03MacroAnalyst
{
    private readonly ILogger _logger;
    private readonly NavSyncOptions _options;
    private readonly IIsinProgressStore _progress;
    private readonly IWeeklyAnalyticsReportsProvider _reports;
    private readonly IMacroContextStore _contexts;
    private readonly IFundMetadataProvider _metadata;
    private readonly IStep03MacroAnalystPromptsProvider _promptsProvider;
    private readonly IMacroAnalystAgent _agent;
    private readonly MacroContextSingleFlight _singleFlight;
    private readonly IPipelineSignals _signals;
    private readonly IStepEventPublisher _stepEvents;

    /// <summary>Company filter from configuration — scopes the category universe.</summary>
    private Company _family = new(string.Empty);

    /// <summary>Week of the trading date that raised the run.</summary>
    private IsoWeek _isoWeek = IsoWeek.Empty;

    /// <summary>This fund's record as step 2 stored it; null until it loads.</summary>
    private FundRecord? _fund;

    /// <summary>The latest reports up to the week; null when there are none, or until loaded.</summary>
    private WeeklyAnalyticsReports? _weeklyReports;

    /// <summary>
    /// The week the loaded reports are from — the fund's week, or an earlier one when no
    /// later export exists. A miss computes the context for this week, never the fund's.
    /// </summary>
    private IsoWeek _reportsWeek = IsoWeek.Empty;

    /// <summary>The context this fund runs with — found by the lookup, or computed on a miss.</summary>
    private MacroContext? _context;

    /// <summary>Every fund's metadata as the agent's category universe; assembled on a miss.</summary>
    private DataLoaderOutput? _universe;

    /// <summary>The prompts the agent runs with; assembled on a miss.</summary>
    private Step03MacroAnalystPrompts? _prompts;

    /// <summary>This fund's progress row as this run last wrote it; null until it is started.</summary>
    private IsinProgressEntity? _progressRow;

    /// <summary>Runs from the start, so a reported duration covers every phase.</summary>
    private readonly Stopwatch _stepDuration = new();

    public Step03MacroAnalystHandler(
        ILogger logger,
        NavSyncOptions options,
        IIsinProgressStore progress,
        IWeeklyAnalyticsReportsProvider reports,
        IMacroContextStore contexts,
        IFundMetadataProvider metadata,
        IStep03MacroAnalystPromptsProvider promptsProvider,
        IMacroAnalystAgent agent,
        MacroContextSingleFlight singleFlight,
        IPipelineSignals signals,
        IStepEventPublisher stepEvents)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(promptsProvider);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(singleFlight);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(stepEvents);

        _logger = logger;
        _options = options;
        _progress = progress;
        _reports = reports;
        _contexts = contexts;
        _metadata = metadata;
        _promptsProvider = promptsProvider;
        _agent = agent;
        _singleFlight = singleFlight;
        _signals = signals;
        _stepEvents = stepEvents;
    }

    /// <inheritdoc />
    public async Task<bool> BeginProcessingAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug(
            "Step 3 begin — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        try
        {
            // A start, not step 1's claim: that one clears the row, and step 2's output on
            // it is what this step is about to read.
            _progressRow = await _progress
                .StartStep03Async(signal.Isin, signal.RunId, ct)
                .ConfigureAwait(false);

            if (_progressRow is null)
            {
                // Delivery is at-least-once, so a signal can outlive its run — the run has
                // ended, or a newer one holds the fund. Either way nothing here is ours.
                _logger.Info(
                    "Step 3 skipped — isin={0}, runId={1} is no longer the fund's run",
                    signal.Isin.Value, signal.RunId.Value);

                return false;
            }

            _logger.Debug("Step 3 started — isin={0}", signal.Isin.Value);

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
                ex, "Step 3 start failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task LoadFundAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 3 load — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _isoWeek = IsoWeek.FromDate(signal.NavDate);

            _family = new Company(_options.CompanyFilter);

            // The run-wide read is enough: step 1 mints a run id per fund, so the run's
            // output holds this fund's record and nothing else.
            var step02 = await _progress
                .ReadStepOutputAsync(StepId.MetricsCalculator, signal.RunId, ct)
                .ConfigureAwait(false);

            // The start already proved the run is ours, so an empty column is not a late
            // signal — step 2 stored no record. A failure, so the next signal retries it.
            _fund = step02?.Funds.FirstOrDefault(f => f.Isin == signal.Isin)
                ?? throw new InvalidOperationException(
                    $"Step 2 stored no record for ISIN {signal.Isin.Value} in run {signal.RunId.Value}.");

            _context = await LookUpContextAsync(signal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: without the record, or without any context to run with, there is
            // nothing for the rest of the step to do.
            _logger.Error(ex, "Step 3 load failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task AssembleAgentInputAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // A hit needs nothing assembled — the context is already the agent's result.
        if (_context is not null)
        {
            _logger.Debug("Step 3 assemble skipped — isin={0}, context found", signal.Isin.Value);
            return;
        }

        _logger.Debug("Step 3 assemble — isin={0}, week {1}", signal.Isin.Value, _isoWeek);

        try
        {
            // Every fund's categories, not this fund's: the agent restricts the LLM to the
            // categories some fund actually has, and the context serves the whole week.
            var funds = await _metadata
                .ListMetadataAsync(_family, _isoWeek, ct)
                .ConfigureAwait(false);

            // An empty universe would filter every catalyst and theme away and store that
            // empty context for the whole week — fail instead, so the next signal retries.
            if (funds.Count == 0)
                throw new InvalidOperationException(
                    $"No fund metadata for company '{_family.Value}' — the category universe is empty.");

            _universe = WrapAsUniverse(funds, signal.RunId);

            _prompts = await _promptsProvider.GetPromptsAsync(ct).ConfigureAwait(false);

            _logger.Debug(
                "Step 3 assembled — isin={0}, {1} fund(s) in the universe", signal.Isin.Value, funds.Count);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Step 3 assemble failed — isin={0}", signal.Isin.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MacroContext> RunAgentAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (_fund is null)
            throw new InvalidOperationException("LoadFundAsync must run before RunAgentAsync.");

        // A hit: the context was computed for an earlier fund of the week.
        if (_context is not null)
            return _context;

        // A miss always comes from reports that were found — no reports and no stored
        // context already failed the load.
        if (_weeklyReports is null || _universe is null || _prompts is null)
            throw new InvalidOperationException("AssembleAgentInputAsync must run before RunAgentAsync.");

        var key = _weeklyReports.WeeklySummary.RunId;

        _logger.Debug(
            "Step 3 compute — isin={0}, expected week {1}, computing for week {2}, weekly summary run {3}",
            signal.Isin.Value, _isoWeek, _reportsWeek, key);

        try
        {
            _context = await _singleFlight
                .RunAsync(key, ComputeOnceAsync, ct)
                .ConfigureAwait(false);

            return _context;
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // The agent's validation failures land here too — a fund failure like any other.
            _logger.Error(ex, "Step 3 compute failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (_context is null || _fund is null)
            throw new InvalidOperationException("RunAgentAsync must run before PersistAsync.");

        if (_progressRow is null)
            throw new InvalidOperationException("BeginProcessingAsync must run before PersistAsync.");

        _logger.Debug("Step 3 persist — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            // The record as step 2 left it: step 3's output is per week, so it adds nothing
            // to the fund, and the append-only chain carries the record on unchanged.
            _progressRow = await _progress
                .SaveStepOutputAsync(signal.Isin, StepId.MacroAnalyst, signal.RunId, WrapAsOutput(_fund, signal.RunId), ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 3 persisted — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
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
            _logger.Error(ex, "Step 3 persist failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task EmitDoneAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 3 emit — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            // Identity only, no payload, same as steps 1 and 2: a reader takes the record back
            // off the row and the context from the store.
            await _signals
                .PublishAsync(new Step03DoneSignal(signal.Isin, signal.NavDate, signal.RunId), ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 3 emitted — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

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
            _logger.Error(ex, "Step 3 emit failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Completing a row this run never held would free another run's fund.
        if (_progressRow is null)
            throw new InvalidOperationException("BeginProcessingAsync must run before CompleteAsync.");

        _logger.Debug("Step 3 complete — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .CompleteStep03Async(signal.Isin, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 3 completed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
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
                ex, "Step 3 complete failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task FailAsync(Step02DoneSignal signal, Exception error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(error);

        // No row of ours to free: the start was refused — a newer run holds the fund — or
        // never ran. Failing it anyway would release that other run's lock.
        if (_progressRow is null)
        {
            _logger.Debug("Step 3 fail skipped — isin={0} was never started", signal.Isin.Value);
            return;
        }

        _logger.Debug("Step 3 fail — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);

        try
        {
            _progressRow = await _progress
                .FailStep03Async(signal.Isin, error.Message, ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 3 failed — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
        catch (Exception ex)
        {
            // Swallowed, cancellation included: the caller is already handling the failure
            // that brought it here, and a throw would replace it. The row stays in flight.
            _logger.Error(
                ex, "Step 3 fail could not be recorded — isin={0}, runId={1}", signal.Isin.Value, signal.RunId.Value);
        }
    }

    /// <inheritdoc />
    public async Task<int> FailOrphanedRunsAsync(DateTimeOffset startedBefore, CancellationToken ct = default)
    {
        _logger.Debug("Step 3 orphaned-run recovery — startedBefore={0:u}", startedBefore);

        var orphaned = await _progress
            .ListOrphanedStep03Async(startedBefore, ct)
            .ConfigureAwait(false);

        var failed = 0;

        foreach (var row in orphaned)
        {
            try
            {
                // A fail, not a reset: it clears the stale lock and keeps the dedup anchor,
                // so the next NAV sync sees the fund as changed and raises it again. A context
                // the dead process never stored needs no cleanup — the next miss computes it.
                await _progress
                    .FailStep03Async(
                        new Isin(row.Isin),
                        "Orphaned run — the process ended before step 3 finished; reprocessed on the next NAV sync.",
                        ct)
                    .ConfigureAwait(false);

                failed++;

                _logger.Info("Step 3 orphaned run failed — isin={0}, runId={1}", row.Isin, row.RunId?.Value);
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
                _logger.Error(ex, "Step 3 orphaned run could not be failed — isin={0}", row.Isin);
            }
        }

        _logger.Debug("Step 3 orphaned-run recovery done — {0} of {1} failed", failed, orphaned.Count);

        return failed;
    }

    /// <inheritdoc />
    public async Task<MacroContext?> ReadContextAsync(IsoWeek isoWeek, CancellationToken ct = default)
    {
        _logger.Debug("Step 3 read — week {0}", isoWeek);

        try
        {
            return await _contexts.GetNewestAsync(isoWeek, ct).ConfigureAwait(false);
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
            _logger.Error(ex, "Step 3 read failed — week {0}", isoWeek);

            return null;
        }
    }

    /// <summary>
    /// The lookup in the step 3 plan's order: the latest reports up to the fund's week name
    /// the context to look for; a stored context of a later week than those reports wins
    /// over computing from them; without any reports, the newest stored context stands in.
    /// </summary>
    /// <returns>The context found, or <c>null</c> for a miss that must be computed.</returns>
    /// <exception cref="InvalidOperationException">No reports up to the week and no context to fall back on.</exception>
    private async Task<MacroContext?> LookUpContextAsync(Step02DoneSignal signal, CancellationToken ct)
    {
        _weeklyReports = await _reports.GetLatestAsync(_isoWeek, ct).ConfigureAwait(false);

        if (_weeklyReports is not null)
        {
            var key = _weeklyReports.WeeklySummary.RunId;
            _reportsWeek = IsoWeek.From(_weeklyReports.WeeklySummary.PeriodIsoWeek);

            var stored = await _contexts.GetAsync(key, ct).ConfigureAwait(false);

            _logger.Debug(
                "Step 3 lookup — isin={0}, expected week {1}, reports week {2}, weekly summary run {3}: {4}",
                signal.Isin.Value, _isoWeek, _reportsWeek, key, stored is null ? "miss" : "hit");

            if (stored is not null)
            {
                LogIfEarlierWeek(signal, stored);
                return stored;
            }

            // Reports older than the fund's week, but a context newer than them is stored —
            // computed from a later export that has since been replaced. The newer one wins:
            // recomputing from older reports would step the week's analysis backwards.
            if (_reportsWeek.Value != _isoWeek.Value)
            {
                var newest = await _contexts.GetNewestAsync(_isoWeek, ct).ConfigureAwait(false);

                if (newest is not null && string.CompareOrdinal(newest.IsoWeek, _reportsWeek.Value) > 0)
                {
                    LogIfEarlierWeek(signal, newest);
                    return newest;
                }

                _logger.Info(
                    "Step 3 fallback — isin={0}, no reports for {1}; computing from the reports of {2}",
                    signal.Isin.Value, _isoWeek, _reportsWeek);
            }

            return null;
        }

        // No reports up to the week: the newest context of the week or earlier keeps the
        // chain moving. A stored context for this very week can still be found here — the
        // reports being unreachable does not make the context computed from them wrong.
        var fallback = await _contexts.GetNewestAsync(_isoWeek, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No analytics reports for {_isoWeek} or earlier, and no macro context stored.");

        LogIfEarlierWeek(signal, fallback);

        return fallback;
    }

    /// <summary>
    /// Logged, not recorded: the fund record has no field for the week its context is from,
    /// and the context carries its own week and run ids.
    /// </summary>
    private void LogIfEarlierWeek(Step02DoneSignal signal, MacroContext context)
    {
        if (context.IsoWeek == _isoWeek.Value)
            return;

        _logger.Info(
            "Step 3 fallback — isin={0}, expected week {1}; using the stored context of {2} (weekly summary run {3})",
            signal.Isin.Value, _isoWeek, context.IsoWeek, context.SourceRunIds.WeeklySummaryRunId);
    }

    /// <summary>
    /// The miss, run once per key across every handler: re-checks the store first, since a
    /// fund that missed a moment ago may have stored the context since, then calls the LLM
    /// and stores the result.
    /// </summary>
    private async Task<MacroContext> ComputeOnceAsync(CancellationToken ct)
    {
        var key = _weeklyReports!.WeeklySummary.RunId;

        var stored = await _contexts.GetAsync(key, ct).ConfigureAwait(false);
        if (stored is not null)
            return stored;

        var computed = await _agent
            .RunInMemoryAsync(
                _weeklyReports.WeeklySummary,
                _weeklyReports.SubstitutionChain,
                _weeklyReports.RotationTargets,
                _universe!,
                _reportsWeek,
                _prompts!,
                ct)
            .ConfigureAwait(false);

        await _contexts.SaveAsync(computed, ct).ConfigureAwait(false);

        _logger.Info(
            "Step 3 context computed and stored — week {0}, weekly summary run {1}, regime {2}",
            computed.IsoWeek, key, computed.MacroRegime);

        return computed;
    }

    /// <summary>
    /// Wraps every fund's metadata as the <see cref="DataLoaderOutput"/> the agent reads its
    /// categories from, stamped with the reports' week — the agent checks the two match, and
    /// a context is only ever computed for the week its reports are from.
    /// </summary>
    private DataLoaderOutput WrapAsUniverse(IReadOnlyList<FundMetadata> funds, PipelineRunId runId) =>
        WrapAsOutput(
            funds.Select(m => new FundRecord
            {
                Isin = m.Isin,
                Metadata = m,
                NavBuckets = Array.Empty<NavBucket>(),
            }).ToList(),
            _reportsWeek,
            runId);

    private DataLoaderOutput WrapAsOutput(FundRecord fund, PipelineRunId runId) => WrapAsOutput([fund], _isoWeek, runId);

    private DataLoaderOutput WrapAsOutput(IReadOnlyList<FundRecord> funds, IsoWeek week, PipelineRunId runId) => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek = week.Value,
        Family = _family.Value,
        RunId = runId,
        ConfigVersion = "1.0.0",
        Funds = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        CashAvailableKr = 0m,
        DataQuality = new DataQuality(),
    };

    /// <summary>
    /// Reports this step's progress for one fund. Elapsed time is measured from the start,
    /// so it spans every phase rather than the one that happened to report.
    /// </summary>
    /// <param name="kind">Started carries no duration; there is nothing elapsed yet.</param>
    /// <param name="isin">The fund the tick is about.</param>
    /// <param name="message">Why it failed, for a failure; otherwise null.</param>
    private void Report(StepEventKind kind, Isin isin, string? message = null)
        => _stepEvents.Publish(new StepEvent(
            StepId.MacroAnalyst,
            kind,
            isin,
            message,
            kind == StepEventKind.Started ? null : _stepDuration.Elapsed));
}
