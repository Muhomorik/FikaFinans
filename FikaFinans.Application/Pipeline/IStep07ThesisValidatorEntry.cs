using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 6's done-signal enters step 7, holding step 7's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. Step 6's entry point's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep07ThesisValidator"/>
/// <seealso cref="IStep06CatalystTaggerEntry"/>
public interface IStep07ThesisValidatorEntry
{
    /// <summary>
    /// Runs one fund through step 7, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step06DoneSignal signal, CancellationToken ct = default);
}
