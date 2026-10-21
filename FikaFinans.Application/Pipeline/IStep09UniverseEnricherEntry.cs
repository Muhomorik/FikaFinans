namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where every trigger enters step 9 — step 8's done-signal, the daily timer, the Run All
/// button. Checks the barrier and runs the pass when it has closed, so no trigger has to
/// know what either involves.
/// </summary>
/// <remarks>
/// Unlike steps 1–8's entry points it takes no signal: a pass is over the whole universe,
/// so which fund finished last does not matter — only that every fund has.
/// </remarks>
/// <seealso cref="Steps.IStep09Barrier"/>
/// <seealso cref="Steps.IStep09UniverseEnricher"/>
public interface IStep09UniverseEnricherEntry
{
    /// <summary>
    /// Runs one pass over the universe, or returns without starting one when the barrier is
    /// still open or the universe is empty.
    /// </summary>
    /// <param name="trigger">Who asked — for the log; every trigger runs the same check.</param>
    /// <returns><c>true</c> when a pass ran and was stored.</returns>
    Task<bool> HandleAsync(Step09Trigger trigger, CancellationToken ct = default);
}

/// <summary>What asked step 9 to check its barrier.</summary>
public enum Step09Trigger
{
    /// <summary>A fund completed step 8 — the normal case, as soon as the last one does.</summary>
    Step08Done,

    /// <summary>
    /// The daily timer, before step 10 — catches barriers that closed without a step 8
    /// completion. The Run All button is the same call.
    /// </summary>
    Timer,
}
