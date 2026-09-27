namespace FikaFinans.Application.Pipeline;

/// <summary>
/// The receiving half of <see cref="IStepEventPublisher"/> — every step's ticks on one
/// stream, so a view subscribes for its own step and ignores the rest.
/// </summary>
/// <seealso cref="IStepEventPublisher"/>
public interface IStepEventSource
{
    IObservable<StepEvent> Events { get; }
}
