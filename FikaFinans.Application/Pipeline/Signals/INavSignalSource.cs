namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Source seam exposing raised <see cref="NavChangeSignal"/>s as an Rx stream.
/// The WPF app subscribes to <see cref="Signals"/> to auto-trigger scoped
/// pipeline runs. Local-only: in Azure the consumer is the Queue Storage
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
