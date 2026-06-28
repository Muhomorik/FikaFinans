namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// The shared "brain" of NAV-change detection: compares each fund's latest
/// trading date (from <see cref="ILatestNavProvider"/>) against the durable
/// dedup anchor (<c>IsinProgressEntity.LatestProcessedNavDate</c>) and decides
/// which funds have a NAV change worth processing.
/// </summary>
/// <remarks>
/// Application-layer service — environment-agnostic. Identical logic runs
/// locally (YR DB source, Rx sink) and in Azure (YR HTTP source, Queue Storage
/// sink); only the injected <see cref="ILatestNavProvider"/> and
/// <see cref="INavSignalPublisher"/> differ. Implementations are library code:
/// they await with <c>ConfigureAwait(false)</c> and honour cancellation.
/// </remarks>
public interface INavChangeDetector
{
    /// <summary>
    /// Detect — but do not publish — the NAV-change signals for the configured
    /// company. A fund qualifies when it has no progress row, its anchor is
    /// null, or its latest NAV date is strictly newer than the anchor.
    /// </summary>
    /// <param name="ct">Cancels the underlying reads.</param>
    /// <returns>The will-raise signals; never null, possibly empty.</returns>
    Task<IReadOnlyList<NavChangeSignal>> DetectAsync(CancellationToken ct = default);

    /// <summary>
    /// Describe — for display only — <em>every</em> candidate fund for the
    /// configured company, classifying each (<see cref="NavSyncStatusKind"/>) by
    /// its latest YR NAV date vs the <c>IsinProgressEntity</c> anchor and state.
    /// Returns all candidates (not just the will-raise ones) so the local status
    /// grid can show the full picture. Publishes nothing.
    /// </summary>
    /// <param name="ct">Cancels the underlying reads.</param>
    /// <returns>One status row per candidate fund; never null, possibly empty.</returns>
    Task<IReadOnlyList<NavSyncStatusRow>> DescribeAsync(CancellationToken ct = default);

    /// <summary>
    /// Detect and, if any signals were raised, publish them through the
    /// configured <see cref="INavSignalPublisher"/>.
    /// </summary>
    /// <param name="ct">Cancels the underlying reads + publish.</param>
    /// <returns>The published set (empty when nothing was raised).</returns>
    Task<IReadOnlyList<NavChangeSignal>> DetectAndPublishAsync(CancellationToken ct = default);
}