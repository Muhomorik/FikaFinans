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

    /// <summary>Writes <c>Step01Json</c> on the progress row and the new raw NAV rows.</summary>
    Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Emits the step-2 trigger — after the write, never before.</summary>
    Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Reads a finished run's output back for display. A read, never a trigger — opening a
    /// view must not start anything.
    /// </summary>
    /// <returns><c>null</c> when that run wrote no output.</returns>
    Task<StepOutput?> ReadOutputAsync(PipelineRunId runId, CancellationToken ct = default);
}
