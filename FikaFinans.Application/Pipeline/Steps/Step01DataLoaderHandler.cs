using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

public sealed class Step01DataLoaderHandler : IStep01DataLoader
{
    private readonly NavSyncOptions _options;
    private readonly IFundMetadataProvider _metadata;
    private readonly IIsinProgressRepository _isinProgress;
    private readonly IStreamingPipelineGateway _gateway;
    private readonly IFundsRepository _funds;
    private readonly IPositionsRepository _positions;
    private readonly IPathsService _paths;
    private readonly IDataLoaderAgent _agent;
    private readonly ILogger _logger;

    public Step01DataLoaderHandler(
        NavSyncOptions options,
        IFundMetadataProvider metadata,
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
        ArgumentNullException.ThrowIfNull(isinProgress);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(funds);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _metadata = metadata;
        _isinProgress = isinProgress;
        _gateway = gateway;
        _funds = funds;
        _positions = positions;
        _paths = paths;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Marks the fund's progress row in-flight before anything is read — the half of
    /// <c>ClaimIsinProgressAsync</c> that must precede the fetch, so an at-least-once redelivery
    /// is detectable instead of racing a second full fetch.
    /// </summary>
    public Task BeginProcessingAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <summary>Reads the identity slice and the NAV history delta through the fetch seam.</summary>
    public Task LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Computes the bucketed and rolling-window metrics from the mirrored series, giving the
    /// agent the inputs that arrive as producer CSVs today.
    /// </summary>
    public Task AssembleAgentInputAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <summary>Joins the assembled inputs via <see cref="IDataLoaderAgent.RunInMemory"/>.</summary>
    public Task RunAgentAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Writes the two outputs — <c>Step01Json</c> on the progress row (latest-only) and the new
    /// raw NAV rows (accumulating).
    /// </summary>
    public Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Emits the step-2 trigger, after the write so a crash between the two replays this step
    /// rather than advancing the chain past an output that was never written.
    /// </summary>
    public Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();
}
