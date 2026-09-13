using System.Globalization;

using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

public sealed class Step01DataLoaderHandler : IStep01DataLoader
{
    private readonly NavSyncOptions _options;
    private readonly IFundMetadataProvider _metadata;
    private readonly IFundSummaryProvider _summary;
    private readonly IFundSnapshotProvider _snapshots;
    private readonly IHoldingsProvider _holdingsProvider;
    private readonly IPortfolioStructureProvider _structureProvider;
    private readonly IPipelineRunIdFactory _runIdFactory;
    private readonly IIsinProgressRepository _isinProgress;
    private readonly IStreamingPipelineGateway _gateway;
    private readonly IFundsRepository _funds;
    private readonly IPositionsRepository _positions;
    private readonly IPathsService _paths;
    private readonly IDataLoaderAgent _agent;
    private readonly ILogger _logger;

    /// <summary>
    /// Company filter from configuration — only funds belonging to it are processed. 
    /// Holds one company today. Empty filters means no filtering.
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

    /// <summary>Layer pinnings for the whole portfolio.</summary>
    private PortfolioStructure _portfolioStructure = new() { Pinnings = Array.Empty<PinnedFund>() };

    /// <summary>Every held position plus cash — portfolio-scoped, not per fund.</summary>
    private PositionsParseResult _holdings = PositionsParseResult.Empty;

    /// <summary>What the agent joined, kept for the phases that persist and emit it.</summary>
    private DataLoaderOutput? _agentOutput;

    public Step01DataLoaderHandler(
        NavSyncOptions options,
        IFundMetadataProvider metadata,
        IFundSummaryProvider summary,
        IFundSnapshotProvider snapshots,
        IHoldingsProvider holdingsProvider,
        IPortfolioStructureProvider structureProvider,
        IPipelineRunIdFactory runIdFactory,
        IIsinProgressRepository isinProgress,
        IStreamingPipelineGateway gateway,
        IFundsRepository funds,
        IPositionsRepository positions,
        IPathsService paths,
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
        ArgumentNullException.ThrowIfNull(isinProgress);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(funds);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);
        
        _options = options;
        _metadata = metadata;
        _summary = summary;
        _snapshots = snapshots;
        _holdingsProvider = holdingsProvider;
        _structureProvider = structureProvider;
        _runIdFactory = runIdFactory;
        _isinProgress = isinProgress;
        _gateway = gateway;
        _funds = funds;
        _positions = positions;
        _paths = paths;
        _agent = agent;
        _logger = logger;
    }

    /// <inheritdoc />
    // TODO: claim the progress row through _isinProgress — state to Processing, stamp
    // ProcessingStartedAt, clear the later columns. Losing the race means returning
    // without starting a run, so the caller needs to hear which happened.
    public Task BeginProcessingAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public async Task LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

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

        // Portfolio-scoped, unlike everything else this phase reads: cash, the frozen list
        // and the downstream weight sums all span the whole book.
        //
        // TODO: live halt exposure — the join throws held_isin_not_in_metadata for any held
        // ISIN it has no metadata for, and _fundMetadata covers the signal's fund alone, so
        // a second holding trips it. Either metadata spans every held ISIN or the rule stops
        // applying to per-ISIN runs; the plan asks the same of pin matching, unanswered.
        _holdings = await _holdingsProvider.GetHoldingsAsync(ct).ConfigureAwait(false);

        // Pinnings are configuration, not producer data, so they are read per run rather
        // than per fund — the join needs the whole set to resolve one fund's layer.
        _portfolioStructure = await _structureProvider.GetStructureAsync(ct).ConfigureAwait(false);

        // TODO: NAV history delta — nothing reads or writes the local mirror yet, so there
        // is no raw series to diff against.
        //
        // TODO: the summary and snapshot seams read YieldRaccoon's database straight, which
        // is the opposite of the cache-first mirror the plan picked. Mirror-backed
        // implementations replace them once the mirror holds rows.
    }

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

        // Left unkeyed when null so the agent hits its own "snapshot missing" warning
        // rather than being handed an entry whose metrics are all null anyway.
        _fundSnapshots = snapshot is null
            ? new Dictionary<Isin, FundSnapshot>()
            : new Dictionary<Isin, FundSnapshot> { [signal.Isin] = snapshot };

        if (snapshot is null)
            _logger.Debug("Step 1 no snapshot — isin={0}", signal.Isin.Value);
    }

    /// <inheritdoc />
    public Task<DataLoaderOutput> RunAgentAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        // Everything the agent joins is already in memory by now — this phase opens no file and
        // touches no database. _fundMetadata, _navBuckets and _fundSnapshots arrive from the
        // earlier phases through the three fetch seams, so their source swaps (SQLite today,
        // REST later) without this call changing.
        _agentOutput = _agent.RunInMemory(
            _family, _isoWeek, _runId,
            _fundMetadata, _navBuckets, _fundSnapshots, _holdings, _portfolioStructure);

        return Task.FromResult(_agentOutput);
    }

    /// <inheritdoc />
    // TODO: store _agentOutput and close the progress row. The old Run path wrote JSON under
    // IPathsService; where it lands now is open, and the NAV mirror write belongs here too.
    public Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <inheritdoc />
    // TODO: emit Step01DoneSignal through _gateway so step 2 picks the fund up, and record a
    // StepEvent — neither type exists yet, and the event's shape is still deferred.
    public Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();
}
