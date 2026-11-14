using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 10 for the whole portfolio, split into phases like step 9's — one run over every
/// fund's step 9 record and the portfolio around them, so there is no row to start, complete
/// or fail.
/// </summary>
/// <remarks>
/// No barrier of its own: step 9 already waited for every fund, and step 10 runs on whatever
/// <c>Step09Json</c> the rows hold — each fund's last completed run.
/// </remarks>
public interface IStep10PortfolioConstructor
{
    /// <summary>
    /// Starts a run: takes the week the run happens in and mints the run's id. Never touches
    /// a row — the funds' own runs keep them.
    /// </summary>
    /// <returns>The run's id, for the caller's log lines.</returns>
    Task<PipelineRunId> BeginRunAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads step 9's record for every fund in step 9's universe. A fund without one is left
    /// out — never completed, or mid-run after step 1's start cleared it.
    /// </summary>
    /// <returns>
    /// How many funds have a record. Zero means nothing to trade on: the run is reported
    /// done here, and the caller runs no further phase.
    /// </returns>
    Task<int> LoadUniverseAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the portfolio config and the portfolio — holdings, cash, frozen positions,
    /// layers — fresh, so an edit or a settled trade applies to this run.
    /// </summary>
    Task AssembleAgentInputAsync(CancellationToken ct = default);

    /// <summary>The passes via <c>IPortfolioConstructorAgent.RunInMemory</c>. Code-only, no LLM.</summary>
    Task<TradesOutput> RunAgentAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores the run's output on its funds' rows, overwriting any earlier run's slice. The
    /// step is done once it returns.
    /// </summary>
    Task PersistAsync(CancellationToken ct = default);

    /// <summary>
    /// Emits the run's done-signal — after the save, never before, so the bank side reads
    /// this run's trades when the signal arrives. Run id only: the trades are read back
    /// through the store.
    /// </summary>
    Task EmitDoneAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the last stored run back for display. A read, never a trigger — opening a view
    /// must not start anything.
    /// </summary>
    /// <returns><c>null</c> when no run is stored, or the read failed.</returns>
    Task<TradesOutput?> ReadOutputAsync(CancellationToken ct = default);

    /// <summary>
    /// The debug run: a run over what is stored now, returned and never stored — a stored
    /// output is what the bank side acts on. Free: no LLM.
    /// </summary>
    /// <exception cref="InvalidOperationException">No fund in the universe has a step 9 record.</exception>
    Task<TradesOutput> ComputeOutputAsync(CancellationToken ct = default);
}
