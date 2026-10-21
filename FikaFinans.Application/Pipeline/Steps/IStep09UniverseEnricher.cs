using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 9 for the whole universe, split into phases like steps 1–8's — but one pass over
/// every fund rather than one fund's run, so there is no row to start, complete or fail.
/// </summary>
/// <remarks>
/// Whether the pass should run at all — the barrier — is the caller's question, asked
/// first. No done-signal: step 10 runs on its own timer and reads the rows.
/// </remarks>
public interface IStep09UniverseEnricher
{
    /// <summary>
    /// Starts a pass over <paramref name="universe"/>: takes the week the pass runs in and
    /// mints the pass's run id. Never touches a row — the funds' own runs keep them.
    /// </summary>
    /// <param name="universe">The barrier's universe — every fund matching the company filter.</param>
    /// <returns>The pass's run id, for the caller's log lines.</returns>
    Task<PipelineRunId> BeginPassAsync(IReadOnlyList<Isin> universe, CancellationToken ct = default);

    /// <summary>
    /// Reads step 8's record for every fund in the universe. A fund without one — failed
    /// earlier in its chain — is left out: the smaller-but-clean universe.
    /// </summary>
    Task LoadUniverseAsync(CancellationToken ct = default);

    /// <summary>Reads the conviction config — fresh, so an edit applies to this pass.</summary>
    Task AssembleAgentInputAsync(CancellationToken ct = default);

    /// <summary>
    /// The four passes via <c>IUniverseEnricherAgent.RunInMemoryAsync</c> — pairs, peers,
    /// conviction, ranks. One LLM call per Buy with peers, for the differentiators.
    /// </summary>
    Task<DataLoaderOutput> RunAgentAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes each enriched record to <c>Step09Json</c> on its fund's row, overwriting any
    /// earlier pass. The last phase: the step is done once it returns.
    /// </summary>
    Task PersistAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the last pass's records back for display, over the barrier's universe. A read,
    /// never a trigger — opening a view must not start anything.
    /// </summary>
    /// <returns><c>null</c> when no fund has a step 9 record.</returns>
    Task<StepOutput?> ReadOutputAsync(CancellationToken ct = default);

    /// <summary>
    /// The debug run: a pass over the barrier's universe as it stands — closed or not — and
    /// returns it. Writes nothing. Not free: one LLM call per Buy with peers.
    /// </summary>
    /// <returns>The enriched records, stamped with the week the pass runs in.</returns>
    /// <exception cref="InvalidOperationException">No fund in the universe has a step 8 record.</exception>
    Task<DataLoaderOutput> ComputeOutputAsync(CancellationToken ct = default);
}
