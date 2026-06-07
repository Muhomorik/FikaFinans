namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Sink seam for raised <see cref="NavChangeSignal"/>s. The local implementation
/// pushes onto an Rx stream the WPF app subscribes to (see the local signal
/// bus); the Azure implementation will enqueue to Queue Storage.
/// <see cref="NavChangeDetector"/> publishes detected signals through this seam,
/// keeping the detection logic identical across environments.
/// </summary>
public interface INavSignalPublisher
{
    /// <summary>
    /// Publish a batch of detected signals. Implementations decide the
    /// transport (Rx push, queue enqueue). A null/empty batch is a no-op.
    /// </summary>
    /// <param name="signals">The signals to publish; never null.</param>
    /// <param name="ct">Cancels the publish.</param>
    Task PublishAsync(IReadOnlyList<NavChangeSignal> signals, CancellationToken ct = default);
}
