using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 4 for one fund, split into the same phases as steps 1–3 — so the start can gate the
/// reads, and the write can precede the emit.
/// </summary>
public interface IStep04SignalScorer
{
    /// <summary>
    /// Records that the run has reached step 4, before anything is read. Never clears the
    /// row — step 3's output is this step's input.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run is still this signal's and the remaining phases may run;
    /// <c>false</c> when the signal is late or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(Step03DoneSignal signal, CancellationToken ct = default);

    /// <summary>Reads this fund's record back from <c>Step03Json</c>, by the signal's run id.</summary>
    Task LoadFundAsync(Step03DoneSignal signal, CancellationToken ct = default);

    /// <summary>Loads step 4's config through its provider; the record needs no assembly.</summary>
    Task AssembleAgentInputAsync(Step03DoneSignal signal, CancellationToken ct = default);

    /// <summary>Scores the fund via <c>ISignalScorerAgent.RunInMemory</c>.</summary>
    Task<DataLoaderOutput> RunAgentAsync(Step03DoneSignal signal, CancellationToken ct = default);

    /// <summary>Writes the scored record to <c>Step04Json</c> on the fund's progress row.</summary>
    Task PersistAsync(Step03DoneSignal signal, CancellationToken ct = default);

    /// <summary>Emits step 4's done-signal — after the write, never before.</summary>
    Task EmitDoneAsync(Step03DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(Step03DoneSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Fails every run orphaned at step 4 — locked before <paramref name="startedBefore"/>
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
    /// The debug run: scores the records step 3 stored under <paramref name="runId"/>, with
    /// the config a real run loads, and returns them. Neither starts the run nor writes the
    /// store — a debug run must not take the fund's lock, and a replaced output would change
    /// what step 5 reads.
    /// </summary>
    /// <returns>The scored records, stamped with <paramref name="isoWeek"/>.</returns>
    /// <exception cref="InvalidOperationException">Step 3 stored no records for the run.</exception>
    Task<DataLoaderOutput> ComputeOutputAsync(PipelineRunId runId, IsoWeek isoWeek, CancellationToken ct = default);
}
