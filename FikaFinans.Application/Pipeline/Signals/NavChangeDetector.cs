using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using NLog;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Default <see cref="INavChangeDetector"/> — the shared "brain" of NAV-change
/// detection. Compares each fund's latest trading date (from
/// <see cref="ILatestNavProvider"/>) against the durable dedup anchor
/// (<c>IsinProgressEntity.LatestProcessedNavDate</c>) and raises a
/// <see cref="NavChangeSignal"/> for every fund whose NAV has advanced.
/// </summary>
/// <remarks>
/// Environment-agnostic: identical logic runs locally (YR DB source, Rx sink)
/// and in Azure (YR HTTP source, Queue Storage sink) — only the injected
/// <see cref="ILatestNavProvider"/> and <see cref="INavSignalPublisher"/>
/// differ. Library code: awaits with <c>ConfigureAwait(false)</c>.
/// </remarks>
public sealed class NavChangeDetector : INavChangeDetector
{
    /// <summary>Partition key for every <c>IsinProgress</c> row (see the gateway).</summary>
    private const string IsinProgressPartition = "isin-progress";

    private readonly NavSyncOptions _options;
    private readonly ILatestNavProvider _provider;
    private readonly IIsinProgressRepository _isinProgress;
    private readonly INavSignalPublisher _publisher;
    private readonly ILogger _logger;

    public NavChangeDetector(
        NavSyncOptions options,
        ILatestNavProvider provider,
        IIsinProgressRepository isinProgress,
        INavSignalPublisher publisher,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(isinProgress);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _provider = provider;
        _isinProgress = isinProgress;
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavChangeSignal>> DetectAsync(CancellationToken ct = default)
    {
        var (candidates, rowByIsin) = await LoadAsync(ct).ConfigureAwait(false);

        var signals = new List<NavChangeSignal>();
        foreach (var info in candidates)
        {
            // Skip only when we have a committed anchor that is at or beyond the
            // incoming date. Missing row or null anchor → never processed → emit.
            var anchor = rowByIsin.TryGetValue(info.Isin.Value, out var row)
                ? row.LatestProcessedNavDate
                : null;
            var alreadyProcessed = anchor is { } committed && info.NavDate <= committed;
            if (alreadyProcessed)
                continue;

            signals.Add(new NavChangeSignal(info.Isin, info.NavDate));
        }

        _logger.Info(
            "NAV-change detection: {Signals} signal(s) from {Candidates} candidate fund(s) (companyFilter='{Filter}')",
            signals.Count, candidates.Count, _options.CompanyFilter);

        return signals;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavSyncStatusRow>> DescribeAsync(CancellationToken ct = default)
    {
        var (candidates, rowByIsin) = await LoadAsync(ct).ConfigureAwait(false);

        var result = new List<NavSyncStatusRow>(candidates.Count);
        foreach (var info in candidates)
        {
            rowByIsin.TryGetValue(info.Isin.Value, out var row);
            var anchor = row?.LatestProcessedNavDate;

            // In-flight wins the label; otherwise no/null anchor → New (first
            // run), newer-than-anchor → Changed, else caught up → UpToDate.
            var kind =
                row is { State: IsinProgressState.Processing } ? NavSyncStatusKind.Processing
                : anchor is not { } committed ? NavSyncStatusKind.New
                : info.NavDate > committed ? NavSyncStatusKind.Changed
                : NavSyncStatusKind.UpToDate;

            result.Add(new NavSyncStatusRow(
                info.Isin, info.Name, info.CompanyName, info.NavDate, anchor, kind));
        }

        return result;
    }

    /// <summary>
    /// Shared read used by both <see cref="DetectAsync"/> and
    /// <see cref="DescribeAsync"/>: the company-filtered candidate funds and the
    /// progress rows keyed by ISIN.
    /// </summary>
    private async Task<(IReadOnlyList<FundNavInfo> Candidates, IReadOnlyDictionary<string, IsinProgressEntity> RowByIsin)>
        LoadAsync(CancellationToken ct)
    {
        var navInfos = await _provider.GetLatestNavDatesAsync(ct).ConfigureAwait(false);

        var hasFilter = !string.IsNullOrWhiteSpace(_options.CompanyFilter);
        var candidates = (hasFilter
                ? navInfos.Where(n => string.Equals(n.CompanyName, _options.CompanyFilter, StringComparison.OrdinalIgnoreCase))
                : navInfos)
            .ToList();

        var rows = await _isinProgress.QueryPartitionAsync(IsinProgressPartition, ct).ConfigureAwait(false);
        var rowByIsin = rows.ToDictionary(r => r.Isin);

        return (candidates, rowByIsin);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavChangeSignal>> DetectAndPublishAsync(CancellationToken ct = default)
    {
        var signals = await DetectAsync(ct).ConfigureAwait(false);
        if (signals.Count > 0)
            await _publisher.PublishAsync(signals, ct).ConfigureAwait(false);
        return signals;
    }
}
