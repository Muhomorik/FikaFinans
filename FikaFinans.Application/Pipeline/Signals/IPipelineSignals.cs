namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Everything the pipeline sends onward, one overload per signal, so the interface itself
/// is the list — and a new step does not compile until its overload is implemented.
/// </summary>
/// <seealso cref="IPipelineSignalStreams"/>
public interface IPipelineSignals
{
    Task PublishAsync(Step01DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step02DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step03DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step04DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step05DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step06DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step07DoneSignal signal, CancellationToken ct = default);

    Task PublishAsync(Step08DoneSignal signal, CancellationToken ct = default);
}
