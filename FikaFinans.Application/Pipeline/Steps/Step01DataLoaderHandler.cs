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

    public Task Step01LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default)
        => throw new NotImplementedException();
}
