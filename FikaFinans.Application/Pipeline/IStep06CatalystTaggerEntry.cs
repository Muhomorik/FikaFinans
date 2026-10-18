using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 5's done-signal enters step 6, holding step 6's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. Step 5's entry point's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep06CatalystTagger"/>
/// <seealso cref="IStep05MacroAlignerEntry"/>
public interface IStep06CatalystTaggerEntry
{
    /// <summary>
    /// Runs one fund through step 6, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step05DoneSignal signal, CancellationToken ct = default);
}
