using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline;

public interface IPipelineRunner
{
    IObservable<StepEvent> Events { get; }

    Task<bool> RunAllAsync(string family, string isoWeek, PipelineRunId runId, CancellationToken ct = default);

    Task<bool> RunStepAsync(StepId step, string family, string isoWeek, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Streaming variant of <see cref="RunAllAsync"/>. Runs Steps 1 and 3
    /// universe-wide via their agents, then streams every fund through the
    /// per-ISIN block (Steps 2 → 4 → 5 → 6 → 7 → 8) with
    /// <c>Merge(maxConcurrent: <paramref name="maxConcurrent"/>)</c>, writing
    /// boundary JSON files at each per-ISIN step so the per-tab "Run this
    /// step" buttons stay functional after a streaming run. Steps 9 and 10
    /// then run universe-wide. Per-fund <see cref="StepEvent"/>s emit with
    /// <see cref="StepEvent.Isin"/> populated during the block; universe-wide
    /// Started/Succeeded events for the six per-ISIN steps frame the block.
    /// When <paramref name="maxConcurrent"/> is <c>null</c> the implementation
    /// falls back to <see cref="StreamingPipelineOptions.MaxConcurrentFunds"/>
    /// from DI — the production path.
    /// </summary>
    Task<bool> RunAllStreamingAsync(
        string family,
        string isoWeek,
        PipelineRunId runId,
        int? maxConcurrent = null,
        CancellationToken ct = default);
}
