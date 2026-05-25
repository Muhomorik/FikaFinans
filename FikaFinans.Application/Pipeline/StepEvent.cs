using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// One tick from the pipeline orchestrator. <see cref="Isin"/> is null for
/// universe-wide stages (Steps 1, 3, 9, 10) and populated for per-fund ticks
/// emitted by the per-ISIN block (Steps 2, 4, 5, 6, 7, 8). The current runner
/// emits null for every event because no agent is wired to the per-fund path
/// yet — the field exists so the contract is locked before the per-ISIN
/// streaming refactor lands.
/// </summary>
public sealed record StepEvent(
    StepId Step,
    StepEventKind Kind,
    Isin? Isin = null,
    string? Message = null,
    TimeSpan? Duration = null);

public enum StepEventKind
{
    Started,
    Succeeded,
    Failed,
}
