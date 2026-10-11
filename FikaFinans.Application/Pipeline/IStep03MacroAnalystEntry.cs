using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 2's done-signal enters step 3, holding step 3's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. Step 2's entry point's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep03MacroAnalyst"/>
/// <seealso cref="IStep02MetricsCalculatorEntry"/>
public interface IStep03MacroAnalystEntry
{
    /// <summary>
    /// Runs one fund through step 3, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step02DoneSignal signal, CancellationToken ct = default);
}
