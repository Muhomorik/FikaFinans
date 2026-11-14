namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// The receiving half of <see cref="IPipelineSignals"/> — every done-signal on one stream,
/// so a subscriber picks the steps it cares about and adding a step changes nothing here.
/// Local only: in Azure nothing subscribes, because the Functions host reads queue
/// attributes.
/// </summary>
/// <remarks>
/// Typed as <see cref="IPipelineSignal"/>, not <see cref="IStepDoneSignal"/>: steps 1–8 hand
/// off one fund each, but <see cref="Step09DoneSignal"/> is one per pass and names no fund.
/// Subscribers filter by the concrete signal anyway.
/// </remarks>
/// <seealso cref="IPipelineSignals"/>
public interface IPipelineSignalStreams
{
    IObservable<IPipelineSignal> Signals { get; }
}
