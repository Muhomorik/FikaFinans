using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 7's done-signal enters step 8, holding step 8's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. Step 7's entry point's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep08Recommender"/>
/// <seealso cref="IStep07ThesisValidatorEntry"/>
public interface IStep08RecommenderEntry
{
    /// <summary>
    /// Runs one fund through step 8, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step07DoneSignal signal, CancellationToken ct = default);
}
