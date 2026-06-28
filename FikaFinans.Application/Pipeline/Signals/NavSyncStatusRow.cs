using System.Diagnostics;

using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// One row of the local NAV-sync status grid: a candidate fund (after the
/// company filter) with its latest YR NAV date compared against the
/// <c>IsinProgressEntity</c> dedup anchor, plus a display <see cref="Kind"/>.
/// Produced by <see cref="NavChangeDetector.DescribeAsync"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="NavChangeSignal"/> (will-raise funds only), a status row
/// is emitted for <em>every</em> candidate so the grid shows the full picture.
/// <see cref="LastProcessedNavDate"/> is null when the fund has no progress row
/// or has never completed (the first-run case → <see cref="NavSyncStatusKind.New"/>).
/// </remarks>
[DebuggerDisplay("{Isin.Value,nq} {Kind} (YR {LatestNavDate.Date,nq:yyyy-MM-dd})")]
public sealed record NavSyncStatusRow(
    Isin Isin,
    string Name,
    string CompanyName,
    DateTimeOffset LatestNavDate,
    DateTimeOffset? LastProcessedNavDate,
    NavSyncStatusKind Kind);