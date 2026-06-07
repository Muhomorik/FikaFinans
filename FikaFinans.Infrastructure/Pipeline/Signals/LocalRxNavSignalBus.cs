using System.Reactive.Linq;
using System.Reactive.Subjects;
using FikaFinans.Application.Pipeline.Signals;

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

    /// <inheritdoc />
    public IObservable<NavChangeSignal> Signals => _signals.AsObservable();

    /// <inheritdoc />
    public Task PublishAsync(IReadOnlyList<NavChangeSignal> signals, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signals);

        // Serialize emissions so concurrent publishers can't interleave
        // OnNext calls (same guard PipelineRunner.Emit uses).
        lock (_gate)
        {
            foreach (var signal in signals)
            {
                ct.ThrowIfCancellationRequested();
                _signals.OnNext(signal);
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _signals.Dispose();
}
