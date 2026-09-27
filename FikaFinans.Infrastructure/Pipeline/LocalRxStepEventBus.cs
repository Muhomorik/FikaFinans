using System.Reactive.Linq;
using System.Reactive.Subjects;

using FikaFinans.Application.Pipeline;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline;

/// <summary>
/// Local in-process <see cref="IStepEventPublisher"/> over one hot
/// <see cref="Subject{T}"/>, so every step reports onto the stream the UI renders.
/// </summary>
/// <remarks>
/// Register as a singleton — a publisher and a subscriber on separate instances would not
/// share a stream.
/// </remarks>
public sealed class LocalRxStepEventBus : IStepEventPublisher, IStepEventSource, IDisposable
{
    private readonly Subject<StepEvent> _events = new();
    private readonly Lock _gate = new();
    private readonly ILogger _logger;

    public LocalRxStepEventBus(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IObservable<StepEvent> Events => _events.AsObservable();

    /// <inheritdoc />
    public void Publish(StepEvent stepEvent)
    {
        ArgumentNullException.ThrowIfNull(stepEvent);

        _logger.Trace(
            "{0} {1} — isin={2}, {3}",
            stepEvent.Step, stepEvent.Kind, stepEvent.Isin?.Value ?? "—", stepEvent.Duration);

        // Serialize emissions so concurrent publishers can't interleave OnNext calls
        // (the same guard PipelineRunner.Emit uses).
        lock (_gate)
        {
            _events.OnNext(stepEvent);
        }
    }

    public void Dispose() => _events.Dispose();
}
