using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 8 for one fund, split into the same phases as steps 1–7 — so the start can gate the
/// reads, and the write can precede the emit.
/// </summary>
public interface IStep08Recommender
{
    /// <summary>
    /// Records that the run has reached step 8, before anything is read. Never clears the
    /// row — step 7's output is this step's input.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run is still this signal's and the remaining phases may run;
    /// <c>false</c> when the signal is late or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Reads this fund's record back from <c>Step07Json</c>, by the signal's run id. The only
    /// input — signal, thesis, catalyst and held flag all arrive on the record.
    /// </summary>
    Task LoadFundAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Nothing to assemble — the record arrives validated. Kept so the phases match the other
    /// steps'.
    /// </summary>
    Task AssembleAgentInputAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Recommends for the fund via <c>IRecommenderAgent.RunInMemory</c> — a deterministic
    /// mapping, no LLM. The agent is synchronous; the task is already finished on return.
    /// </summary>
    Task<DataLoaderOutput> RunAgentAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>Writes the recommended record to <c>Step08Json</c> on the fund's progress row.</summary>
    Task PersistAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Emits step 8's done-signal — after the complete, never before, so the fund already
    /// reads finished to step 9's barrier when the signal arrives.
    /// </summary>
    Task EmitDoneAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends the run successfully: frees the fund's row and advances its dedup anchor. Only
    /// while step 8 is the chain tail — called after the write and before the emit.
    /// </summary>
    Task CompleteAsync(Step07DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(Step07DoneSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Fails every run orphaned at step 8 — locked before <paramref name="startedBefore"/>
    /// by a process that has since ended. Clears the stale locks and keeps the dedup anchors,
    /// so the next NAV sync raises those funds again and they are reprocessed.
    /// </summary>
    /// <param name="startedBefore">When the current process started.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many orphaned runs were failed.</returns>
    Task<int> FailOrphanedRunsAsync(DateTimeOffset startedBefore, CancellationToken ct = default);

    /// <summary>
    /// Reads a finished run's output back for display. A read, never a trigger — opening a
    /// view must not start anything.
    /// </summary>
    /// <returns><c>null</c> when that run wrote no output.</returns>
    Task<StepOutput?> ReadOutputAsync(PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// The debug run: recommends for the records step 7 stored under <paramref name="runId"/>
    /// and returns them. Neither starts the run nor writes the store. Free — no model call.
    /// </summary>
    /// <returns>The recommended records, stamped with <paramref name="isoWeek"/>.</returns>
    /// <exception cref="InvalidOperationException">Step 7 stored no records for the run.</exception>
    Task<DataLoaderOutput> ComputeOutputAsync(PipelineRunId runId, IsoWeek isoWeek, CancellationToken ct = default);
}
