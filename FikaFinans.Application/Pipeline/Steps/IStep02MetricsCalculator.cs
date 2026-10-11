using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 2 for one fund, split into the same phases as step 1 — so the start can gate the
/// reads, and the write can precede the emit.
/// </summary>
public interface IStep02MetricsCalculator
{
    /// <summary>
    /// Records that the run has reached step 2, before anything is read. Never clears the
    /// row — step 1's output is this step's input.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run is still this signal's and the remaining phases may run;
    /// <c>false</c> when the signal is late or the store could not be reached.
    /// </returns>
    Task<bool> BeginProcessingAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>Reads this fund's record back from <c>Step01Json</c>, by the signal's run id.</summary>
    Task LoadFundAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>Loads step 2's config through its provider; the record needs no assembly.</summary>
    Task AssembleAgentInputAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>Computes the metrics via <c>IMetricsCalculatorAgent.RunInMemory</c>.</summary>
    Task<DataLoaderOutput> RunAgentAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>Writes the enriched record to <c>Step02Json</c> on the fund's progress row.</summary>
    Task PersistAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>Emits step 2's done-signal — after the write, never before.</summary>
    Task EmitDoneAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends the run successfully: frees the fund's row and advances its dedup anchor. Only
    /// while step 2 is the chain tail — called after the emit, never before.
    /// </summary>
    Task CompleteAsync(Step01DoneSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that threw after the start: frees the fund's row and records why, so the
    /// next signal retries it. Never throws — it runs while another failure is in flight.
    /// </summary>
    /// <param name="signal">The signal whose run failed.</param>
    /// <param name="error">What ended the run; its message is stored on the row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(Step01DoneSignal signal, Exception error, CancellationToken ct = default);

    /// <summary>
    /// Reads a finished run's output back for display. A read, never a trigger — opening a
    /// view must not start anything.
    /// </summary>
    /// <returns><c>null</c> when that run wrote no output.</returns>
    Task<StepOutput?> ReadOutputAsync(PipelineRunId runId, CancellationToken ct = default);
}
