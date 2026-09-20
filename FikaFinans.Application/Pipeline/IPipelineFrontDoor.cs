using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where a NAV-change signal enters the pipeline, holding step 1's phases in order so
/// whatever received the signal does not have to know what they are.
/// </summary>
/// <seealso cref="Steps.IStep01DataLoader"/>
public interface IPipelineFrontDoor
{
    /// <summary>
    /// Runs one fund through step 1, or returns without starting when another run already
    /// holds that fund.
    /// </summary>
    Task HandleAsync(NavChangeSignal signal, CancellationToken ct = default);
}
