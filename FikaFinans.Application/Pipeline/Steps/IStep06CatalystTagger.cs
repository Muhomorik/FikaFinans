using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 6 for one fund, split into the same phases as steps 1–5 — so the start can gate the
/// reads, and the write can precede the emit.
/// </summary>
public interface IStep06CatalystTagger
{
    /// <summary>
    /// Records that the run has reached step 6, before anything is read. Never clears the
    /// row — step 5's output is this step's input.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run is still this signal's and the remaining phases may run;
    /// <c>false</c> when the signal is late or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(Step05DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Reads this fund's record back from <c>Step05Json</c>, by the signal's run id, and the
    /// newest macro context step 3 stored up to the trading date's week.
    /// </summary>
    Task LoadFundAsync(Step05DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Nothing to assemble — the record arrives aligned, the catalysts arrive with the context.
    /// Kept so the phases match the other steps'.
    /// </summary>
    Task AssembleAgentInputAsync(Step05DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Tags the fund via <c>ICatalystTaggerAgent.RunInMemoryAsync</c> against the context's
    /// catalysts. Calls the LLM whenever the fund has a category and the week has a catalyst.
    /// </summary>
    Task<DataLoaderOutput> RunAgentAsync(Step05DoneSignal signal, CancellationToken ct = default);

    /// <summary>Writes the tagged record to <c>Step06Json</c> on the fund's progress row.</summary>
    Task PersistAsync(Step05DoneSignal signal, CancellationToken ct = default);

    /// <summary>Emits step 6's done-signal — after the write, never before.</summary>
    Task EmitDoneAsync(Step05DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(Step05DoneSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Fails every run orphaned at step 6 — locked before <paramref name="startedBefore"/>
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
    /// The debug run: tags the records step 5 stored under <paramref name="runId"/> against
    /// the context a real run reads for <paramref name="isoWeek"/>, and returns them. Neither
    /// starts the run nor writes the store. It spends money: every fund with a category calls
    /// the LLM when the week has a catalyst.
    /// </summary>
    /// <returns>The tagged records, stamped with <paramref name="isoWeek"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Step 5 stored no records for the run, or no context is stored up to the week.
    /// </exception>
    Task<DataLoaderOutput> ComputeOutputAsync(PipelineRunId runId, IsoWeek isoWeek, CancellationToken ct = default);
}
