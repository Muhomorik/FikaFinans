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
    public DateTimeOffset? NavDate { get; init; }
    public int CurrentStep { get; init; }
    public DateTimeOffset? LatestProcessedNavDate { get; init; }
    public DateTimeOffset? ProcessingStartedAt { get; init; }
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
