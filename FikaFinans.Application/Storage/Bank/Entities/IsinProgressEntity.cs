using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for the per-ISIN progress record. PartitionKey is the
/// constant <c>"isin-progress"</c>; RowKey is the ISIN. The same row
/// doubles as the in-flight processing lock (<see cref="State"/>,
/// <see cref="ProcessingStartedAt"/>) and the inline step-output store
/// (<see cref="Step01Json"/>…<see cref="Step09Json"/>).
/// </summary>
/// <remarks>
/// State machine and run-boundary semantics live in
/// <see href="../../../../Docs/backend-nav-sync-plan.md">backend-nav-sync-plan.md</see>
/// §"Progress Table" and §"Step Outputs". Step03Json is always null in
/// the local-Rx shape because Step 3 is universe-wide and produces no
/// per-ISIN output; the column survives for symmetry with the Phase 2
/// queue-driven flow where every step writes its own column.
/// </remarks>
public sealed class IsinProgressEntity : TableEntity
{
    public string Isin { get; init; } = string.Empty;
    public IsinProgressState State { get; init; }
    public PipelineRunId? RunId { get; init; }

    /// <summary>
    /// Trading date of the run currently in flight for this ISIN. Set when the
    /// row is claimed (<see cref="State"/> Free → Processing) to the
    /// <c>navDate</c> that triggered the run; only meaningful while
    /// <see cref="State"/> is <see cref="IsinProgressState.Processing"/>.
    /// Null when no run is in flight.
    /// </summary>
    /// <remarks>
    /// Deliberately distinct from <see cref="LatestProcessedNavDate"/>: this is
    /// the optimistic "working on" date, not the committed "done through" date,
    /// so a crashed run never advances the dedup anchor. See
    /// backend-nav-sync-plan.md §"Progress Table".
    /// </remarks>
    public DateTimeOffset? NavDate { get; init; }

    public int CurrentStep { get; init; }

    /// <summary>
    /// Newest trading date for which this ISIN's per-ISIN pipeline (Steps 1–9)
    /// completed successfully. The durable dedup anchor: an incoming signal is
    /// processed only when its <c>navDate</c> is strictly newer than this value.
    /// Null until the first successful run.
    /// </summary>
    /// <remarks>
    /// Advanced to <see cref="NavDate"/> only on <em>per-fund</em> success
    /// (the fund was not in <c>PerIsinBlockResult.FailedFunds</c>); failed funds
    /// keep their prior value so the next signal re-raises them. Survives across
    /// runs and app restarts.
    /// </remarks>
    public DateTimeOffset? LatestProcessedNavDate { get; init; }

    /// <summary>
    /// UTC timestamp captured when the row moved to
    /// <see cref="IsinProgressState.Processing"/>. Drives stuck-row recovery:
    /// a row whose <see cref="State"/> is still Processing past a threshold
    /// (after a crash or host eviction) is reset to Free by the janitor.
    /// Cleared (null) when the row is released to Free.
    /// </summary>
    public DateTimeOffset? ProcessingStartedAt { get; init; }

    /// <summary>
    /// Last error message stamped when this ISIN's per-ISIN chain threw mid-run,
    /// recorded via <c>IStreamingPipelineGateway.MarkFundFailedAsync</c>.
    /// Paired with <see cref="AttemptCount"/> for diagnostics; preserved across
    /// runs until the next attempt overwrites it.
    /// </summary>
    public string? LastError { get; init; }

    public int AttemptCount { get; init; }

    public string? Step01Json { get; init; }
    public string? Step02Json { get; init; }
    public string? Step03Json { get; init; }
    public string? Step04Json { get; init; }
    public string? Step05Json { get; init; }
    public string? Step06Json { get; init; }
    public string? Step07Json { get; init; }
    public string? Step08Json { get; init; }
    public string? Step09Json { get; init; }
}
