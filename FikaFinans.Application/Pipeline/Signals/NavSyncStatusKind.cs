namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Display classification of a fund in the local NAV-sync status grid, derived
/// by <see cref="NavChangeDetector.DescribeAsync"/> from the fund's latest YR
/// NAV date vs its <c>IsinProgressEntity</c> anchor + state.
/// </summary>
/// <remarks>
/// <see cref="New"/> and <see cref="Changed"/> are the will-raise rows (a signal
/// fires for them); <see cref="UpToDate"/> and <see cref="Processing"/> do not.
/// </remarks>
public enum NavSyncStatusKind
{
    /// <summary>No progress row yet, or a null anchor — the first-run case.</summary>
    New,

    /// <summary>Latest YR NAV date is strictly newer than the committed anchor.</summary>
    Changed,

    /// <summary>Anchor is at or beyond the latest YR NAV date — nothing to do.</summary>
    UpToDate,

    /// <summary>A run is currently in flight for this fund (row state is Processing).</summary>
    Processing,
}
