using System.Reactive.Linq;
using System.Reactive.Subjects;

using FikaFinans.Application.Pipeline.Signals;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Signals;

/// <summary>
/// Local in-process implementation of both step-signal seams over one hot
/// <see cref="Subject{T}"/> carrying every done-signal. The Azure implementation replaces
/// it with a queue client, publishing each signal to its own step queue.
/// </summary>
/// <remarks>
/// Register as a singleton so publisher and subscriber share one stream. A new step costs
/// one overload — the subject, the publish path and the stream are shared.
/// </remarks>
public sealed class LocalRxPipelineSignalBus : IPipelineSignals, IPipelineSignalStreams, IDisposable
{
    private readonly Subject<IStepDoneSignal> _signals = new();
    private readonly Lock _gate = new();
    private readonly ILogger _logger;

    public LocalRxPipelineSignalBus(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IObservable<IStepDoneSignal> Signals => _signals.AsObservable();

    /// <inheritdoc />
    public Task PublishAsync(Step01DoneSignal signal, CancellationToken ct = default)
        => PublishCoreAsync(signal, ct);

    /// <summary>
    /// The one publish path. Every overload lands here, so ordering and logging stay
    /// identical across steps.
    /// </summary>
    private Task PublishCoreAsync(IStepDoneSignal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ct.ThrowIfCancellationRequested();

        _logger.Trace(
            "{0} published: {1} @ {2:yyyy-MM-dd} run {3}",
            signal.GetType().Name, signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        // Serialize emissions so concurrent publishers can't interleave OnNext calls.
        lock (_gate)
        {
            _signals.OnNext(signal);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _signals.Dispose();
}
