using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 3 for one fund, split into the same phases as steps 1 and 2 — a per-fund hop that
/// does per-week work. The week's macro context is looked up, and computed only on a miss.
/// </summary>
public interface IStep03MacroAnalyst
{
    /// <summary>
    /// Records that the run has reached step 3, before anything is read. Never clears the
    /// row — step 2's output is this step's input.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run is still this signal's and the remaining phases may run;
    /// <c>false</c> when the signal is late or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Reads this fund's record back from <c>Step02Json</c>, and looks up the context for the
    /// week of the signal's trading date: by the week's reports when they exist, otherwise
    /// the newest stored context of an earlier week. Fails when there is neither.
    /// </summary>
    Task LoadFundAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// On a miss only: loads every fund's metadata as the category universe, and the prompts.
    /// A no-op when the context was found.
    /// </summary>
    Task AssembleAgentInputAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// On a miss only: computes the context via <c>IMacroAnalystAgent.RunInMemoryAsync</c>
    /// and stores it — once per week's reports, however many funds miss together.
    /// </summary>
    /// <returns>The context this fund runs with, found or computed.</returns>
    Task<MacroContext> RunAgentAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>Writes the fund's record, unchanged, to <c>Step03Json</c> on its progress row.</summary>
    Task PersistAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>Emits step 3's done-signal — after the write, never before.</summary>
    Task EmitDoneAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends the run successfully: frees the fund's row and advances its dedup anchor. Only
    /// while step 3 is the chain tail — called after the emit, never before.
    /// </summary>
    Task CompleteAsync(Step02DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(Step02DoneSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Fails every run orphaned at step 3 — locked before <paramref name="startedBefore"/>
    /// by a process that has since ended. Clears the stale locks and keeps the dedup anchors,
    /// so the next NAV sync raises those funds again and they are reprocessed.
    /// </summary>
    /// <param name="startedBefore">When the current process started.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many orphaned runs were failed.</returns>
    Task<int> FailOrphanedRunsAsync(DateTimeOffset startedBefore, CancellationToken ct = default);

    /// <summary>
    /// Reads the week's context back for display: the newest stored for
    /// <paramref name="isoWeek"/> or an earlier week — the same lookup a fund of that week
    /// falls back on. A read, never a trigger — opening a view must not start anything.
    /// </summary>
    /// <returns><c>null</c> when nothing is stored, or the store could not be read.</returns>
    Task<MacroContext?> ReadContextAsync(IsoWeek isoWeek, CancellationToken ct = default);
}
