using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Fetch;
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
    private readonly IIsinProgressRepository _isinProgress;
    private readonly IStreamingPipelineGateway _gateway;
    private readonly IFundsRepository _funds;
    private readonly IPositionsRepository _positions;
    private readonly IPathsService _paths;
    private readonly IDataLoaderAgent _agent;
    private readonly ILogger _logger;

    // In-flight state for one signal, filled phase by phase. Every one starts empty and stays
    // empty until its fetch seam exists — the agent must never be handed a value read from disk
    // at join time.
    private Company _family = new(string.Empty);
    private IsoWeek _isoWeek = new(string.Empty);
    private PipelineRunId _runId = new(string.Empty);

    private IReadOnlyList<FundMetadata> _fundMetadata = Array.Empty<FundMetadata>();
    private IReadOnlyDictionary<Isin, IReadOnlyList<NavBucket>> _navBuckets = new Dictionary<Isin, IReadOnlyList<NavBucket>>();
    private IReadOnlyDictionary<Isin, FundSnapshot> _fundSnapshots = new Dictionary<Isin, FundSnapshot>();
    private PortfolioStructure _portfolioStructure = new() { Pinnings = Array.Empty<PinnedFund>() };
    private PositionsParseResult _holdings = new()
    {
        Holdings = Array.Empty<Position>(),
        CashAvailableKr = 0m,
        Warnings = Array.Empty<string>(),
        TotalRowCount = 0,
    };

    private DataLoaderOutput? _agentOutput;

    public Step01DataLoaderHandler(
        NavSyncOptions options,
        IFundMetadataProvider metadata,
        IFundSummaryProvider summary,
        IFundSnapshotProvider snapshots,
        IIsinProgressRepository isinProgress,
        IStreamingPipelineGateway gateway,
        IFundsRepository funds,
        IPositionsRepository positions,
        IPathsService paths,
        IDataLoaderAgent agent,
        ILogger logger)
    {
        // TODO (01-dataloader.md) — not yet expressible, no type exists:
        //   fetch seam        identity slice + NAV history delta
        //   IPipelineSignals  emit Step01DoneSignal
        //   StepEvent sink    shape deferred
        //   run id            minting seam is an open question in the parent plan
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(snapshots);
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
        _isinProgress = isinProgress;
        _gateway = gateway;
        _funds = funds;
        _positions = positions;
        _paths = paths;
        _agent = agent;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task BeginProcessingAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public async Task LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // TODO: _family is still empty, and the SQLite provider treats a company mismatch as
        // "out of scope" — so this returns null for every fund until the identity seam exists.
        // The miss is logged because it is indistinguishable from an unknown ISIN.
        var metadata = await _metadata
            .GetMetadataAsync(signal.Isin, _family, _isoWeek, ct)
            .ConfigureAwait(false);

        if (metadata is null)
            _logger.Debug("Step 1 metadata miss — isin={0}, company='{1}'", signal.Isin.Value, _family.Value);

        _fundMetadata = metadata is null ? Array.Empty<FundMetadata>() : [metadata];

        // TODO: NAV history delta — the mirrored-series read has no seam yet.
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
        // REST later) without this call changing. The rest are still the empty defaults,
        // wherever the seam that fills them has not been built yet.

        // TODO: _family — no seam; CompanyFilter is the detector's, not this step's.
        // TODO: _isoWeek — no seam; derived from the signal's NavDate once that rule is settled.
        // TODO: _runId — minting seam is still open (see the constructor TODO).
        // TODO: _holdings — from IPositionsRepository, filtered to this signal's ISIN.
        // TODO: _portfolioStructure — portfolio_structure.md has no Application-level parser seam.
        _agentOutput = _agent.RunInMemory(
            _family, _isoWeek, _runId,
            _fundMetadata, _navBuckets, _fundSnapshots, _holdings, _portfolioStructure);

        return Task.FromResult(_agentOutput);
    }

    /// <inheritdoc />
    public Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();
}
