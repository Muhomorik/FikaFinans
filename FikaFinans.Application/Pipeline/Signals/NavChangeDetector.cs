using FikaFinans.Application.Storage.Bank;
using NLog;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// The shared "brain" of NAV-change detection. Compares each fund's latest
/// trading date (from <see cref="ILatestNavProvider"/>) against the durable
/// dedup anchor (<c>IsinProgressEntity.LatestProcessedNavDate</c>) and raises a
/// <see cref="NavChangeSignal"/> for every fund whose NAV has advanced.
/// </summary>
/// <remarks>
/// Environment-agnostic: identical logic runs locally (YR DB source, Rx sink)
/// and in Azure (YR HTTP source, Queue Storage sink) — only the injected
/// <see cref="ILatestNavProvider"/> and <see cref="INavSignalPublisher"/>
/// differ. Library code: awaits with <c>ConfigureAwait(false)</c>.
/// </remarks>
public sealed class NavChangeDetector
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

    /// <summary>
    /// Detect — but do not publish — the NAV-change signals for the configured
    /// company. A fund qualifies when it has no progress row, its anchor is
    /// null, or its latest NAV date is strictly newer than the anchor.
    /// </summary>
    public async Task<IReadOnlyList<NavChangeSignal>> DetectAsync(CancellationToken ct = default)
    {
        var navInfos = await _provider.GetLatestNavDatesAsync(ct).ConfigureAwait(false);

        var hasFilter = !string.IsNullOrWhiteSpace(_options.CompanyFilter);
        var candidates = (hasFilter
                ? navInfos.Where(n => string.Equals(n.CompanyName, _options.CompanyFilter, StringComparison.OrdinalIgnoreCase))
                : navInfos)
            .ToList();

        var rows = await _isinProgress.QueryPartitionAsync(IsinProgressPartition, ct).ConfigureAwait(false);
        var anchorByIsin = rows.ToDictionary(r => r.Isin, r => r.LatestProcessedNavDate);

        var signals = new List<NavChangeSignal>();
        foreach (var info in candidates)
        {
            // Skip only when we have a committed anchor that is at or beyond the
            // incoming date. Missing row or null anchor → never processed → emit.
            var alreadyProcessed =
                anchorByIsin.TryGetValue(info.Isin.Value, out var anchor)
                && anchor is { } committed
                && info.NavDate <= committed;
            if (alreadyProcessed)
                continue;

            signals.Add(new NavChangeSignal(info.Isin, info.NavDate));
        }

        _logger.Info(
            "NAV-change detection: {Signals} signal(s) from {Candidates} candidate fund(s) (companyFilter='{Filter}')",
            signals.Count, candidates.Count, _options.CompanyFilter);

        return signals;
    }

    /// <summary>
    /// Detect and, if any signals were raised, publish them through the
    /// configured <see cref="INavSignalPublisher"/>. Returns the published set.
    /// </summary>
    public async Task<IReadOnlyList<NavChangeSignal>> DetectAndPublishAsync(CancellationToken ct = default)
    {
        var signals = await DetectAsync(ct).ConfigureAwait(false);
        if (signals.Count > 0)
            await _publisher.PublishAsync(signals, ct).ConfigureAwait(false);
        return signals;
    }
}
