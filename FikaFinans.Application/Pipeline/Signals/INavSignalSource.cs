namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Source seam exposing raised <see cref="NavChangeSignal"/>s as an Rx stream.
/// The local pipeline host subscribes to <see cref="Signals"/> and hands each
/// signal to the front door. Local-only: in Azure the consumer is the Queue Storage
/// trigger, so there is no Rx source there — the publish side
/// (<see cref="INavSignalPublisher"/>) is what differs by environment.
/// </summary>
public interface INavSignalSource
{
    /// <summary>
    /// Hot stream of signals as they are published. Late subscribers do not
    /// receive signals raised before they subscribed.
    /// </summary>
    IObservable<NavChangeSignal> Signals { get; }
}
