using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;

using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// <see cref="IStep09Barrier"/> over the same two reads <see cref="NavChangeDetector"/>
/// classifies funds from: the latest trading dates and the progress rows.
/// </summary>
/// <remarks>
/// <see cref="NavChangeDetector.DescribeAsync"/>'s status alone is not enough: a failed run
/// keeps the anchor, so a failed fund reads <c>Changed</c> — or <c>New</c> after a failed
/// first run — the same as one not yet started. The row tells them apart: a fail leaves its
/// trading date and error behind.
/// </remarks>
public sealed class Step09Barrier : IStep09Barrier
{
    /// <summary>Partition key for every <c>IsinProgress</c> row (see the gateway).</summary>
    private const string IsinProgressPartition = "isin-progress";

    private readonly ILogger _logger;
    private readonly NavSyncOptions _options;
    private readonly ILatestNavProvider _provider;
    private readonly IIsinProgressRepository _isinProgress;

    public Step09Barrier(
        ILogger logger,
        NavSyncOptions options,
        ILatestNavProvider provider,
        IIsinProgressRepository isinProgress)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _isinProgress = isinProgress ?? throw new ArgumentNullException(nameof(isinProgress));
    }

    /// <inheritdoc />
    public async Task<Step09BarrierState> CheckAsync(CancellationToken ct = default)
    {
        var navInfos = await _provider.GetLatestNavDatesAsync(ct).ConfigureAwait(false);
        var funds = navInfos.Where(n => _options.MatchesCompany(new Company(n.CompanyName))).ToList();

        var rows = await _isinProgress.QueryPartitionAsync(IsinProgressPartition, ct).ConfigureAwait(false);
        var rowByIsin = rows.ToDictionary(r => r.Isin, StringComparer.Ordinal);

        var failed = new List<Isin>();
        var holding = new List<Step09BarrierHold>();
        foreach (var fund in funds)
        {
            rowByIsin.TryGetValue(fund.Isin.Value, out var row);

            switch (Classify(fund, row))
            {
                case Finish.Done:
                    break;
                case Finish.Failed:
                    failed.Add(fund.Isin);
                    break;
                case Finish.Processing:
                    holding.Add(new Step09BarrierHold(fund.Isin, NavSyncStatusKind.Processing));
                    break;
                default:
                    holding.Add(new Step09BarrierHold(fund.Isin,
                        row?.LatestProcessedNavDate is null ? NavSyncStatusKind.New : NavSyncStatusKind.Changed));
                    break;
            }
        }

        var state = new Step09BarrierState(funds.Select(f => f.Isin).ToList(), failed, holding);

        if (state.IsClosed)
        {
            _logger.Info(
                "Step 9 barrier closed — {0} fund(s), {1} failed: [{2}] (companyFilter='{3}')",
                state.Universe.Count, failed.Count, string.Join(", ", failed.Select(i => i.Value)),
                _options.CompanyFilter);
        }
        else
        {
            _logger.Info(
                "Step 9 barrier open — {0} of {1} fund(s) holding: [{2}] (companyFilter='{3}')",
                holding.Count, state.Universe.Count,
                string.Join(", ", holding.Select(h => $"{h.Isin.Value} {h.Status}")),
                _options.CompanyFilter);
        }

        return state;
    }

    private static Finish Classify(FundNavInfo fund, IsinProgressEntity? row)
    {
        if (row is { State: IsinProgressState.Processing })
            return Finish.Processing;

        // Step 8's complete is the only thing that moves the anchor.
        if (row?.LatestProcessedNavDate is { } anchor && anchor >= fund.NavDate)
            return Finish.Done;

        // A fail frees the row on the trading date it was running, error recorded. An older
        // date means the latest one has not been tried yet.
        if (row is { LastError: not null, NavDate: { } tried } && tried >= fund.NavDate)
            return Finish.Failed;

        return Finish.NotRun;
    }

    private enum Finish
    {
        Done,
        Failed,
        Processing,
        NotRun,
    }
}
