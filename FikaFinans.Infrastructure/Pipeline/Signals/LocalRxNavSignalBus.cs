using System.Reactive.Linq;
using System.Reactive.Subjects;
using FikaFinans.Application.Pipeline.Signals;
using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Signals;

/// <summary>
/// Local in-process implementation of both signal seams over a single hot
/// <see cref="Subject{T}"/>. <see cref="NavChangeDetector"/> publishes through
/// <see cref="INavSignalPublisher"/>; the WPF app subscribes via
/// <see cref="INavSignalSource"/>. Mirrors the <c>Subject</c>/<c>AsObservable</c>
/// surface used by <c>BankSimulator</c> / <c>SettlementEngine</c>.
/// </summary>
/// <remarks>
/// Register as a singleton so publisher and subscriber share one stream.
/// In Azure this whole class is replaced by a queue publisher + queue trigger.
/// </remarks>
public sealed class LocalRxNavSignalBus : INavSignalPublisher, INavSignalSource, IDisposable
{
    private readonly Subject<NavChangeSignal> _signals = new();
    private readonly object _gate = new();
    private readonly ILogger _logger;

    public LocalRxNavSignalBus(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IObservable<NavChangeSignal> Signals => _signals.AsObservable();

    /// <inheritdoc />
    public Task PublishAsync(IReadOnlyList<NavChangeSignal> signals, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (signals.Count == 0)
            return Task.CompletedTask;

        _logger.Debug("NavSignal bus: publishing {0} signal(s)", signals.Count);

        // Serialize emissions so concurrent publishers can't interleave
        // OnNext calls (same guard PipelineRunner.Emit uses).
        lock (_gate)
        {
            foreach (var signal in signals)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Trace("NavSignal published: {0} @ {1:yyyy-MM-dd}", signal.Isin.Value, signal.NavDate);
                _signals.OnNext(signal);
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _signals.Dispose();
}
