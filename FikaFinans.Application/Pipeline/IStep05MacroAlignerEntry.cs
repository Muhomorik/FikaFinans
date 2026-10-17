using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 4's done-signal enters step 5, holding step 5's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. Step 4's entry point's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep05MacroAligner"/>
/// <seealso cref="IStep04SignalScorerEntry"/>
public interface IStep05MacroAlignerEntry
{
    /// <summary>
    /// Runs one fund through step 5, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step04DoneSignal signal, CancellationToken ct = default);
}
