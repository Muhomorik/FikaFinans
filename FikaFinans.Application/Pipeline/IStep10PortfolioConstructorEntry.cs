namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where every trigger enters step 10 — step 9's done-signal and the daily timer. Runs the
/// step's phases in order, so no trigger has to know what they involve.
/// </summary>
/// <remarks>
/// No barrier, unlike step 9's entry: step 9 already waited for every fund, and step 10 runs
/// on whatever step 9 records the rows hold. Takes no signal — a run is over the whole
/// portfolio, so which pass triggered it does not matter.
/// </remarks>
/// <seealso cref="Steps.IStep10PortfolioConstructor"/>
public interface IStep10PortfolioConstructorEntry
{
    /// <summary>
    /// Runs step 10 once over the stored step 9 records and the portfolio, and stores the
    /// trades — or returns without trading when no fund has a step 9 record yet.
    /// </summary>
    /// <param name="trigger">Who asked — for the log; every trigger runs the same.</param>
    /// <returns><c>true</c> when a run traded and was stored.</returns>
    Task<bool> HandleAsync(Step10Trigger trigger, CancellationToken ct = default);
}

/// <summary>What asked step 10 to run.</summary>
public enum Step10Trigger
{
    /// <summary>A step 9 pass stored its records — the trades follow the new conviction.</summary>
    Step09Done,

    /// <summary>
    /// The daily timer — catches a day with no step 9 pass, trading on the records stored.
    /// </summary>
    Timer,
}
