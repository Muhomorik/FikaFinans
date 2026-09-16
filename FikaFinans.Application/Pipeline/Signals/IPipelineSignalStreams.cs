namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// The receiving half of <see cref="IPipelineSignals"/> — every done-signal on one stream,
/// so a subscriber picks the steps it cares about and adding a step changes nothing here.
/// Local only: in Azure nothing subscribes, because the Functions host reads queue
/// attributes.
/// </summary>
/// <seealso cref="IPipelineSignals"/>
public interface IPipelineSignalStreams
{
    IObservable<IStepDoneSignal> Signals { get; }
}
