using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 1 for one fund, split into the phases a caller runs in order — so the claim can
/// precede the reads, and the write can precede the emit.
/// </summary>
public interface IStep01DataLoader
{
    /// <summary>Marks the fund's progress row in-flight, before anything is read.</summary>
    /// <returns>
    /// <c>true</c> when the row was claimed and the remaining phases may run;
    /// <c>false</c> when another run already holds it or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Reads the identity slice and the NAV history delta through the fetch seam.</summary>
    Task LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Computes the bucketed and rolling-window metrics from the mirrored series.</summary>
    Task AssembleAgentInputAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Joins the assembled inputs via <c>IDataLoaderAgent.RunInMemory</c>.</summary>
    Task<DataLoaderOutput> RunAgentAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Writes this run's output to <c>Step01Json</c> on the fund's progress row.</summary>
    Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Emits the step-2 trigger — after the write, never before.</summary>
    Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(NavChangeSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Fails every run orphaned at step 1 — locked before <paramref name="startedBefore"/>
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
}
