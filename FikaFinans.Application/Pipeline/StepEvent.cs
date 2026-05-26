using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// One tick from the pipeline orchestrator. <see cref="Isin"/> is null for
/// universe-wide stages (Steps 1, 3, 9, 10) and populated for per-fund ticks
/// emitted by the per-ISIN block (Steps 2, 4, 5, 6, 7, 8). <see cref="Total"/>
/// is set on the universe-wide <see cref="StepEventKind.Started"/> for each of
/// the six per-ISIN steps when emitted from
/// <see cref="PipelineRunner.RunAllStreamingAsync"/>; it carries the number of
/// funds in the streaming universe so the UI can render "Step 4 — 137 / 1500"
/// progress as per-fund Succeeded events trickle in.
/// </summary>
public sealed record StepEvent(
    StepId Step,
    StepEventKind Kind,
    Isin? Isin = null,
    string? Message = null,
    TimeSpan? Duration = null,
    int? Total = null);

public enum StepEventKind
{
    Started,
    Succeeded,
    Failed,
}
