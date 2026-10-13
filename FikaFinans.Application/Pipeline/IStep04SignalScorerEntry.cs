using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 3's done-signal enters step 4, holding step 4's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. Step 3's entry point's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep04SignalScorer"/>
/// <seealso cref="IStep03MacroAnalystEntry"/>
public interface IStep04SignalScorerEntry
{
    /// <summary>
    /// Runs one fund through step 4, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step03DoneSignal signal, CancellationToken ct = default);
}
