using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline;

/// <summary>
/// The local stand-in for the Functions app's trigger bindings: one subscription per Function,
/// each handing its signal to the same entry point a queue trigger would call. Holds no
/// pipeline logic — dedup, claims, run ids and barriers all live behind the entry points.
/// </summary>
/// <remarks>
/// <para>
/// Local only. A Functions host never resolves it, and nothing depends on it: the dependency
/// runs from the host to the entry points, so the cloud's graph is unaffected.
/// </para>
/// <para>
/// Takes no WPF type. Its scheduler is passed explicitly at registration, never the
/// dispatcher scheduler the desktop app registers — signal handling must not run on the UI
/// thread.
/// </para>
/// </remarks>
public sealed class LocalPipelineHost : IDisposable
{
    /// <summary>
    /// How long NAV signals are collected before duplicates are dropped. The detector's pass
    /// publishes in one burst; a fund signalled twice in it reaches the front door once.
    /// </summary>
    private static readonly TimeSpan NavBufferWindow = TimeSpan.FromMilliseconds(250);

    private readonly ILogger _logger;
    private readonly IScheduler _scheduler;
    private readonly INavSignalSource _navSignals;
    private readonly IPipelineSignalStreams _pipelineSignals;
    private readonly IPipelineFrontDoor _frontDoor;
    private readonly IStep02MetricsCalculatorEntry _step02;
    private readonly IStep03MacroAnalystEntry _step03;
    private readonly IStep04SignalScorerEntry _step04;
    private readonly IStep05MacroAlignerEntry _step05;
    private readonly IStep06CatalystTaggerEntry _step06;
    private readonly IStep07ThesisValidatorEntry _step07;
    private readonly IStep08RecommenderEntry _step08;
    private readonly IStep09UniverseEnricherEntry _step09;
    private readonly IStep10PortfolioConstructorEntry _step10;

    private readonly CompositeDisposable _subscriptions = new();
    private CancellationTokenSource _inFlight = new();
    private bool _started;

    public LocalPipelineHost(
        ILogger logger,
        IScheduler scheduler,
        INavSignalSource navSignals,
        IPipelineSignalStreams pipelineSignals,
        IPipelineFrontDoor frontDoor,
        IStep02MetricsCalculatorEntry step02,
        IStep03MacroAnalystEntry step03,
        IStep04SignalScorerEntry step04,
        IStep05MacroAlignerEntry step05,
        IStep06CatalystTaggerEntry step06,
        IStep07ThesisValidatorEntry step07,
        IStep08RecommenderEntry step08,
        IStep09UniverseEnricherEntry step09,
        IStep10PortfolioConstructorEntry step10)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _navSignals = navSignals ?? throw new ArgumentNullException(nameof(navSignals));
        _pipelineSignals = pipelineSignals ?? throw new ArgumentNullException(nameof(pipelineSignals));
        _frontDoor = frontDoor ?? throw new ArgumentNullException(nameof(frontDoor));
        _step02 = step02 ?? throw new ArgumentNullException(nameof(step02));
        _step03 = step03 ?? throw new ArgumentNullException(nameof(step03));
        _step04 = step04 ?? throw new ArgumentNullException(nameof(step04));
        _step05 = step05 ?? throw new ArgumentNullException(nameof(step05));
        _step06 = step06 ?? throw new ArgumentNullException(nameof(step06));
        _step07 = step07 ?? throw new ArgumentNullException(nameof(step07));
        _step08 = step08 ?? throw new ArgumentNullException(nameof(step08));
        _step09 = step09 ?? throw new ArgumentNullException(nameof(step09));
        _step10 = step10 ?? throw new ArgumentNullException(nameof(step10));
    }

    /// <summary>
    /// Subscribes every trigger. Signals published before this are not seen — the buses are
    /// hot and keep nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Already started.</exception>
    public void Start()
    {
        if (_started)
            throw new InvalidOperationException("The local pipeline host is already started.");
        _started = true;

        // NAV queue trigger. The buffer only coalesces a burst; the front door does the real
        // dedup against the fund's progress row.
        _subscriptions.Add(_navSignals.Signals
            .Buffer(NavBufferWindow, _scheduler)
            .SelectMany(batch => batch.DistinctBy(s => s.Isin))
            .Subscribe(signal => Dispatch(1, $"fund {signal.Isin.Value}",
                ct => _frontDoor.HandleAsync(signal, ct))));

        // Steps 2–8: one per-fund hop each, the next step's queue trigger.
        On<Step01DoneSignal>(2, s => s.Isin.Value, (s, ct) => _step02.HandleAsync(s, ct));
        On<Step02DoneSignal>(3, s => s.Isin.Value, (s, ct) => _step03.HandleAsync(s, ct));
        On<Step03DoneSignal>(4, s => s.Isin.Value, (s, ct) => _step04.HandleAsync(s, ct));
        On<Step04DoneSignal>(5, s => s.Isin.Value, (s, ct) => _step05.HandleAsync(s, ct));
        On<Step05DoneSignal>(6, s => s.Isin.Value, (s, ct) => _step06.HandleAsync(s, ct));
        On<Step06DoneSignal>(7, s => s.Isin.Value, (s, ct) => _step07.HandleAsync(s, ct));
        On<Step07DoneSignal>(8, s => s.Isin.Value, (s, ct) => _step08.HandleAsync(s, ct));

        // Step 9: every step 8 completion asks the barrier; only the one that finds every fund
        // finished runs the pass.
        _subscriptions.Add(_pipelineSignals.Signals
            .OfType<Step08DoneSignal>()
            .Subscribe(done => Dispatch(9, $"after {done.Isin.Value} completed step 8",
                ct => _step09.HandleAsync(Step09Trigger.Step08Done, ct))));

        // Step 10: one run per stored step 9 pass.
        _subscriptions.Add(_pipelineSignals.Signals
            .OfType<Step09DoneSignal>()
            .Subscribe(done => Dispatch(10, $"after step 9 pass {done.RunId.Value}",
                ct => _step10.HandleAsync(Step10Trigger.Step09Done, ct))));

        _logger.Info("Local pipeline host started");
    }

    /// <summary>
    /// Cancels every entry point in flight — the Stop button. Work dispatched afterwards gets
    /// a fresh token, so later signals still run.
    /// </summary>
    public void CancelInFlight()
    {
        // The old source is not disposed: work still running holds its token.
        var cancelled = Interlocked.Exchange(ref _inFlight, new CancellationTokenSource());
        cancelled.Cancel();
        _logger.Info("Local pipeline host: in-flight work cancelled");
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _inFlight.Cancel();
        _inFlight.Dispose();
    }

    /// <summary>A per-fund hop: the step's done-signal names the fund it ran.</summary>
    private void On<TSignal>(int step, Func<TSignal, string> isin, Func<TSignal, CancellationToken, Task> handle)
        where TSignal : IPipelineSignal
        => _subscriptions.Add(_pipelineSignals.Signals
            .OfType<TSignal>()
            .Subscribe(signal => Dispatch(step, $"fund {isin(signal)}", ct => handle(signal, ct))));

    /// <summary>
    /// Runs one entry point off the Rx callback — a bus publishes inside the previous step's
    /// emit, so running inline would hold that step up.
    /// </summary>
    /// <remarks>
    /// Nothing is allowed to escape: an entry point rethrows so a queue trigger can retry,
    /// but an exception reaching <c>Subscribe</c> ends the subscription for the session, and
    /// every later signal would be silently dropped.
    /// </remarks>
    private void Dispatch(int step, string cause, Func<CancellationToken, Task> work)
    {
        var ct = Volatile.Read(ref _inFlight).Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await work(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Step {Step} cancelled, {Cause}", step, cause);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Step {Step} failed, {Cause}", step, cause);
            }
        });
    }
}
