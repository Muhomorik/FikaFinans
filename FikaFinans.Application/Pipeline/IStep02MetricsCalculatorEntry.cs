using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where step 1's done-signal enters step 2, holding step 2's phases in order so whatever
/// received the signal — an Rx subscription locally, a queue trigger in Azure — does not
/// have to know what they are. The front door's twin, one step further down the chain.
/// </summary>
/// <seealso cref="Steps.IStep02MetricsCalculator"/>
/// <seealso cref="IPipelineFrontDoor"/>
public interface IStep02MetricsCalculatorEntry
{
    /// <summary>
    /// Runs one fund through step 2, or returns without starting when the signal is late —
    /// its run has ended, or a newer run holds the fund.
    /// </summary>
    Task HandleAsync(Step01DoneSignal signal, CancellationToken ct = default);
}
