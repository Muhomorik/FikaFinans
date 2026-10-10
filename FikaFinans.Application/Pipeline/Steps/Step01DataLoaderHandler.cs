using System.Diagnostics;
using System.Globalization;

using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Default <see cref="IStep01DataLoader"/>. Reads every input through a seam and keeps one
/// fund's run in fields, so it must not be shared between funds.
/// </summary>
public sealed class Step01DataLoaderHandler : IStep01DataLoader
{
    private readonly NavSyncOptions _options;
    private readonly IFundMetadataProvider _metadata;
    private readonly IFundSummaryProvider _summary;
    private readonly IFundSnapshotProvider _snapshots;
    private readonly IHoldingsProvider _holdingsProvider;
    private readonly IPortfolioStructureProvider _structureProvider;
    private readonly IPipelineRunIdFactory _runIdFactory;
    private readonly IIsinProgressStore _progress;
    private readonly IPipelineSignals _signals;
    private readonly IStepEventPublisher _stepEvents;
    private readonly IDataLoaderAgent _agent;
    private readonly ILogger _logger;

    /// <summary>
    /// Company filter from configuration — only funds belonging to it are processed. 
    /// Holds one company today. Empty filters mean no filtering.
    /// </summary>
    private Company _family = new(string.Empty);

    /// <summary>Week of the trading date that raised the signal.</summary>
    private IsoWeek _isoWeek = new(string.Empty);

    /// <summary>Names this run in the output and every log line; empty until the fund loads.</summary>
    private PipelineRunId _runId = new(string.Empty);

    /// <summary>The signal's fund, or empty when it is out of scope or unknown.</summary>
    private IReadOnlyList<FundMetadata> _fundMetadata = Array.Empty<FundMetadata>();

    /// <summary>Two-week windows, keyed by ISIN.</summary>
    private IReadOnlyDictionary<Isin, IReadOnlyList<NavBucket>> _navBuckets = new Dictionary<Isin, IReadOnlyList<NavBucket>>();

    /// <summary>12-week and 1-year metrics, keyed by ISIN. Unkeyed when unavailable.</summary>
    private IReadOnlyDictionary<Isin, FundSnapshot> _fundSnapshots = new Dictionary<Isin, FundSnapshot>();

    /// <summary>Every pinned fund in the portfolio, with the layer it is pinned to.</summary>
    private PortfolioStructure _portfolioStructure = new() { PinnedFunds = Array.Empty<PinnedFund>() };

    /// <summary>Every held position plus cash — portfolio-scoped, not per fund.</summary>
    private PositionsParseResult _holdings = PositionsParseResult.Empty;

    /// <summary>What the agent joined, kept for the phases that persist and emit it.</summary>
    private DataLoaderOutput? _agentOutput;

    /// <summary>This fund's progress row as this run last wrote it; null until it is claimed.</summary>
    private IsinProgressEntity? _progressRow;

    /// <summary>Runs from the claim, so a reported duration covers every phase.</summary>
    private readonly Stopwatch _stepDuration = new();

    public Step01DataLoaderHandler(
        NavSyncOptions options,
        IFundMetadataProvider metadata,
        IFundSummaryProvider summary,
        IFundSnapshotProvider snapshots,
        IHoldingsProvider holdingsProvider,
        IPortfolioStructureProvider structureProvider,
        IPipelineRunIdFactory runIdFactory,
        IIsinProgressStore progress,
        IPipelineSignals signals,
        IStepEventPublisher stepEvents,
        IDataLoaderAgent agent,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(holdingsProvider);
        ArgumentNullException.ThrowIfNull(structureProvider);
        ArgumentNullException.ThrowIfNull(runIdFactory);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(stepEvents);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);
        
        _options = options;
        _metadata = metadata;
        _summary = summary;
        _snapshots = snapshots;
        _holdingsProvider = holdingsProvider;
        _structureProvider = structureProvider;
        _runIdFactory = runIdFactory;
        _progress = progress;
        _signals = signals;
        _stepEvents = stepEvents;
        _agent = agent;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> BeginProcessingAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug(
            "Step 1 begin — isin={0}, navDate={1:yyyy-MM-dd}",
            signal.Isin.Value, signal.NavDate);

        try
        {
            _progressRow = await _progress
                .ClaimAsync(signal.Isin, signal.NavDate, ct)
                .ConfigureAwait(false);

            if (_progressRow is null)
            {
                _logger.Info(
                    "Step 1 skipped — isin={0} already in flight, navDate={1:yyyy-MM-dd}",
                    signal.Isin.Value, signal.NavDate);

                return false;
            }

            _logger.Debug("Step 1 claimed — isin={0}", signal.Isin.Value);

            // Only now is the fund ours to work on, so this is where the step begins as
            // far as anything watching is concerned.
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
                ex, "Step 1 claim failed — isin={0}, navDate={1:yyyy-MM-dd}",
                signal.Isin.Value, signal.NavDate);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 1 load — isin={0}", signal.Isin.Value);

        try
        {
            _isoWeek = ToIsoWeek(signal.NavDate);

            _family = new Company(_options.CompanyFilter);

            _runId = _runIdFactory.NewRunId(signal.Isin, signal.NavDate);

            var metadata = await _metadata
                .GetMetadataAsync(signal.Isin, _family, _isoWeek, ct)
                .ConfigureAwait(false);

            // A miss is indistinguishable from an unknown ISIN, so it is logged rather than
            // thrown — the fund simply produces no record downstream.
            if (metadata is null)
                _logger.Debug("Step 1 metadata miss — isin={0}, company='{1}'", signal.Isin.Value, _family.Value);

            _fundMetadata = metadata is null ? Array.Empty<FundMetadata>() : [metadata];

            // One row per held fund, plus the cash balance available to trade with.
            _holdings = await _holdingsProvider.GetHoldingsAsync(ct).ConfigureAwait(false);

            // Pinned funds are configuration, not producer data, so they are read per run
            // rather than per fund — the join needs the whole set to resolve one fund's layer.
            _portfolioStructure = await _structureProvider.GetStructureAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: the later phases join what this one read, so there is nothing to
            // carry on with. Reported first so the view shows why rather than hanging.
            _logger.Error(ex, "Step 1 load failed — isin={0}", signal.Isin.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<StepOutput?> ReadOutputAsync(
        PipelineRunId runId, CancellationToken ct = default)
    {
        _logger.Debug("Step 1 read — runId={0}", runId.Value);

        try
        {
            return await _progress
                .ReadStepOutputAsync(StepId.DataLoader, runId, ct)
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
            _logger.Error(ex, "Step 1 read failed — runId={0}", runId.Value);

            return null;
        }
    }

    /// <summary>
    /// Reports this step's progress for one fund, the way <c>PipelineRunner</c> does for
    /// the whole run. Elapsed time is measured from the claim, so it spans every phase
    /// rather than the one that happened to report.
    /// </summary>
    /// <param name="kind">Started carries no duration; there is nothing elapsed yet.</param>
    /// <param name="isin"></param>
    /// <param name="message"></param>
    private void Report(StepEventKind kind, Isin isin, string? message = null)
        => _stepEvents.Publish(new StepEvent(
            StepId.DataLoader,
            kind,
            isin,
            message,
            kind == StepEventKind.Started ? null : _stepDuration.Elapsed));

    /// <summary>
    /// Returns the ISO-8601 week label that a trading date falls in.
    /// </summary>
    /// <param name="navDate">Read in its own offset; converting to UTC can move the week.</param>
    /// <returns>A label in <c>YYYY-Www</c> form, for example <c>2026-W18</c>.</returns>
    private static IsoWeek ToIsoWeek(DateTimeOffset navDate)
    {
        var date = navDate.Date;

        return IsoWeek.From(string.Create(
            CultureInfo.InvariantCulture,
            $"{ISOWeek.GetYear(date):D4}-W{ISOWeek.GetWeekOfYear(date):D2}"));
    }

    /// <inheritdoc />
    public async Task AssembleAgentInputAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 1 assemble — isin={0}", signal.Isin.Value);

        try
        {
            var buckets = await _summary
                .GetNavBucketsAsync(signal.Isin, _family, _isoWeek, ct)
                .ConfigureAwait(false);

            // Keyed even when empty: the agent looks buckets up per ISIN and treats a
            // missing key as "no history", which is exactly what an empty list means.
            _navBuckets = new Dictionary<Isin, IReadOnlyList<NavBucket>> { [signal.Isin] = buckets };

            if (buckets.Count == 0)
                _logger.Debug("Step 1 no NAV buckets — isin={0}", signal.Isin.Value);

            var snapshot = await _snapshots
                .GetSnapshotAsync(signal.Isin, _family, _isoWeek, ct)
                .ConfigureAwait(false);

            // Left unkeyed when null, so the agent hits its own "snapshot missing" warning
            // rather than being handed an entry whose metrics are all null anyway.
            _fundSnapshots = snapshot is null
                ? new Dictionary<Isin, FundSnapshot>()
                : new Dictionary<Isin, FundSnapshot> { [signal.Isin] = snapshot };

            if (snapshot is null)
                _logger.Debug("Step 1 no snapshot — isin={0}", signal.Isin.Value);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: these are the metrics the agent scores on, so a run without them
            // would produce a record that looks complete and is not.
            _logger.Error(ex, "Step 1 assemble failed — isin={0}", signal.Isin.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public Task<DataLoaderOutput> RunAgentAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger.Debug("Step 1 join — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

        try
        {
            // Everything the agent joins is already in memory by now — this phase opens no
            // file and touches no database. The earlier phases fill every argument through a
            // fetch seam, so their source swaps (SQLite today, REST later) without this call
            // changing.
            _agentOutput = _agent.RunInMemory(
                _family, _isoWeek, _runId,
                _fundMetadata, _navBuckets, _fundSnapshots, _holdings, _portfolioStructure);

            return Task.FromResult(_agentOutput);
        }
        catch (Exception ex)
        {
            // Catches DataLoaderHaltException too, which IDataLoaderAgent says the caller
            // owns. Reporting it as a failure is the honest reading for now — a halt means
            // the join refused the data, and nothing downstream should run on it.
            _logger.Error(ex, "Step 1 join failed — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Both are set by the phases this one runs after. Reaching here without them is a
        // caller running the phases out of order, not a condition to recover from.
        if (_agentOutput is null)
            throw new InvalidOperationException("RunAgentAsync must run before PersistAsync.");

        if (_progressRow is null)
            throw new InvalidOperationException("BeginProcessingAsync must run before PersistAsync.");

        _logger.Debug(
            "Step 1 persist — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

        try
        {
            _progressRow = await _progress
                .SaveStepOutputAsync(signal.Isin, StepId.DataLoader, _runId, _agentOutput, ct)
                .ConfigureAwait(false);

            _logger.Debug(
                "Step 1 persisted — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown, unlike the claim: EmitDoneAsync must not tell step 2 to read an
            // output that was never stored. The row stays in flight for the janitor.
            _logger.Error(
                ex, "Step 1 persist failed — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Names the run the signal belongs to, so it has to be minted by now. The writing
        // this emitting follows is PersistAsync's, which throws rather than returning quietly
        // when it fails — so a caller that ran the phases in order has already stored the
        // output by the time it reaches here.
        if (string.IsNullOrEmpty(_runId.Value))
            throw new InvalidOperationException("LoadFundAsync must run before EmitDoneAsync.");

        _logger.Debug("Step 1 emit — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

        try
        {
            // Identity only, no payload: step 2 reads the record back off the row, so
            // nothing travelling here can disagree with what was stored.
            await _signals
                .PublishAsync(new Step01DoneSignal(signal.Isin, signal.NavDate, _runId), ct)
                .ConfigureAwait(false);

            _logger.Debug("Step 1 emitted — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

            // The last phase, so the step is done for this fund.
            Report(StepEventKind.Succeeded, signal.Isin);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // The output is stored, but step 2 was never told. Rethrown so the caller can
            // fail the fund; the row stays in flight rather than looking complete.
            _logger.Error(
                ex, "Step 1 emit failed — isin={0}, runId={1}", signal.Isin.Value, _runId.Value);

            Report(StepEventKind.Failed, signal.Isin, ex.Message);

            throw;
        }
    }
}
