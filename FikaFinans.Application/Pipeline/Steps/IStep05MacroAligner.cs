using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 5 for one fund, split into the same phases as steps 1–4 — so the start can gate the
/// reads, and the write can precede the emit.
/// </summary>
public interface IStep05MacroAligner
{
    /// <summary>
    /// Records that the run has reached step 5, before anything is read. Never clears the
    /// row — step 4's output is this step's input.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run is still this signal's and the remaining phases may run;
    /// <c>false</c> when the signal is late or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(Step04DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Reads this fund's record back from <c>Step04Json</c>, by the signal's run id, and the
    /// newest macro context step 3 stored up to the trading date's week.
    /// </summary>
    Task LoadFundAsync(Step04DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Nothing to assemble — the record arrives scored, the themes arrive with the context.
    /// Kept so the phases match the other steps'.
    /// </summary>
    Task AssembleAgentInputAsync(Step04DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Aligns the fund via <c>IMacroAlignerAgent.RunInMemoryAsync</c> against the context's
    /// rotation themes. May call the LLM, when the fund's category matches no theme directly.
    /// </summary>
    Task<DataLoaderOutput> RunAgentAsync(Step04DoneSignal signal, CancellationToken ct = default);

    /// <summary>Writes the aligned record to <c>Step05Json</c> on the fund's progress row.</summary>
    Task PersistAsync(Step04DoneSignal signal, CancellationToken ct = default);

    /// <summary>Emits step 5's done-signal — after the write, never before.</summary>
    Task EmitDoneAsync(Step04DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(Step04DoneSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Fails every run orphaned at step 5 — locked before <paramref name="startedBefore"/>
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
    /// The debug run: aligns the records step 4 stored under <paramref name="runId"/> against
    /// the context a real run reads for <paramref name="isoWeek"/>, and returns them. Neither
    /// starts the run nor writes the store. Unlike steps 2–4's, it can spend money: a fund
    /// with no direct match calls the LLM.
    /// </summary>
    /// <returns>The aligned records, stamped with <paramref name="isoWeek"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Step 4 stored no records for the run, or no context is stored up to the week.
    /// </exception>
    Task<DataLoaderOutput> ComputeOutputAsync(PipelineRunId runId, IsoWeek isoWeek, CancellationToken ct = default);
}
