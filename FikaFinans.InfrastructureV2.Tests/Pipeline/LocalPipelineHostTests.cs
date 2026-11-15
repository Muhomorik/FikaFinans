using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Reactive.Testing;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(LocalPipelineHost))]
public sealed class LocalPipelineHostTests
{
    private static readonly Isin FundIsin = new("LU0000000001");
    private static readonly Isin OtherIsin = new("LU0000000002");
    private static readonly DateTimeOffset Date = new(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly PipelineRunId RunId = new("run-1");

    /// <summary>Dispatch runs on the thread pool, so every wait is bounded.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private IFixture _fixture = null!;
    private TestScheduler _scheduler = null!;
    private FakeTimeProvider _clock = null!;
    private Subject<NavChangeSignal> _navSignals = null!;
    private Subject<IPipelineSignal> _pipelineSignals = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        _scheduler = new TestScheduler();
        _fixture.Inject<IScheduler>(_scheduler);

        // Noon UTC, in a UTC zone — so the timers' local times are plain offsets from here.
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _fixture.Inject<TimeProvider>(_clock);

        _navSignals = new Subject<NavChangeSignal>();
        _fixture.Freeze<Mock<INavSignalSource>>()
            .Setup(s => s.Signals).Returns(_navSignals);

        _pipelineSignals = new Subject<IPipelineSignal>();
        _fixture.Freeze<Mock<IPipelineSignalStreams>>()
            .Setup(s => s.Signals).Returns(_pipelineSignals);
    }

    [TearDown]
    public void TearDown()
    {
        _navSignals.Dispose();
        _pipelineSignals.Dispose();
    }

    private LocalPipelineHost CreateStarted()
    {
        var sut = _fixture.Create<LocalPipelineHost>();
        sut.Start();
        return sut;
    }

    /// <summary>
    /// Moves both clocks together: the host reads the time from one and waits on the other.
    /// </summary>
    private void Advance(TimeSpan by)
    {
        _clock.Advance(by);
        _scheduler.AdvanceBy(by.Ticks);
    }

    /// <summary>How far the noon start is from a timer's time of day.</summary>
    private static TimeSpan UntilToday(TimeOnly at) => at.ToTimeSpan() - TimeSpan.FromHours(12);

    private static TaskCompletionSource<T> NewCapture<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ── Routing: one signal, one entry point ────────────────────────────

    [Test]
    public async Task Start_NavSignal_HandsItToFrontDoorAfterBuffer()
    {
        // Arrange
        var got = NewCapture<NavChangeSignal>();
        _fixture.Freeze<Mock<IPipelineFrontDoor>>()
            .Setup(d => d.HandleAsync(It.IsAny<NavChangeSignal>(), It.IsAny<CancellationToken>()))
            .Callback<NavChangeSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new NavChangeSignal(FundIsin, Date);

        // Act
        _navSignals.OnNext(signal);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step01Done_HandsItToStep02Entry()
    {
        // Arrange
        var got = NewCapture<Step01DoneSignal>();
        _fixture.Freeze<Mock<IStep02MetricsCalculatorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step01DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step01DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step02Done_HandsItToStep03Entry()
    {
        // Arrange
        var got = NewCapture<Step02DoneSignal>();
        _fixture.Freeze<Mock<IStep03MacroAnalystEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step02DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step02DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step02DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step03Done_HandsItToStep04Entry()
    {
        // Arrange
        var got = NewCapture<Step03DoneSignal>();
        _fixture.Freeze<Mock<IStep04SignalScorerEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step03DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step03DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step03DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step04Done_HandsItToStep05Entry()
    {
        // Arrange
        var got = NewCapture<Step04DoneSignal>();
        _fixture.Freeze<Mock<IStep05MacroAlignerEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step04DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step04DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step05Done_HandsItToStep06Entry()
    {
        // Arrange
        var got = NewCapture<Step05DoneSignal>();
        _fixture.Freeze<Mock<IStep06CatalystTaggerEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step05DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step05DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step06Done_HandsItToStep07Entry()
    {
        // Arrange
        var got = NewCapture<Step06DoneSignal>();
        _fixture.Freeze<Mock<IStep07ThesisValidatorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step06DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step06DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step06DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step07Done_HandsItToStep08Entry()
    {
        // Arrange
        var got = NewCapture<Step07DoneSignal>();
        _fixture.Freeze<Mock<IStep08RecommenderEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step07DoneSignal, CancellationToken>((s, _) => got.TrySetResult(s))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        var signal = new Step07DoneSignal(FundIsin, Date, RunId);

        // Act
        _pipelineSignals.OnNext(signal);

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(signal));
    }

    [Test]
    public async Task Start_Step08Done_AsksStep09WithStep08DoneTrigger()
    {
        // Arrange
        var got = NewCapture<Step09Trigger>();
        _fixture.Freeze<Mock<IStep09UniverseEnricherEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step09Trigger>(), It.IsAny<CancellationToken>()))
            .Callback<Step09Trigger, CancellationToken>((t, _) => got.TrySetResult(t))
            .ReturnsAsync(true);
        using var sut = CreateStarted();

        // Act
        _pipelineSignals.OnNext(new Step08DoneSignal(FundIsin, Date, RunId));

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(Step09Trigger.Step08Done));
    }

    [Test]
    public async Task Start_Step09Done_RunsStep10WithStep09DoneTrigger()
    {
        // Arrange
        var got = NewCapture<Step10Trigger>();
        _fixture.Freeze<Mock<IStep10PortfolioConstructorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step10Trigger>(), It.IsAny<CancellationToken>()))
            .Callback<Step10Trigger, CancellationToken>((t, _) => got.TrySetResult(t))
            .ReturnsAsync(true);
        using var sut = CreateStarted();

        // Act
        _pipelineSignals.OnNext(new Step09DoneSignal(RunId));

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(Step10Trigger.Step09Done));
    }

    // ── NAV burst coalescing ────────────────────────────────────────────

    [Test]
    public async Task Start_SameFundTwiceInOneBuffer_FrontDoorSeesItOnce()
    {
        // Arrange
        var calls = new ConcurrentQueue<NavChangeSignal>();
        var bothFunds = NewCapture<bool>();
        _fixture.Freeze<Mock<IPipelineFrontDoor>>()
            .Setup(d => d.HandleAsync(It.IsAny<NavChangeSignal>(), It.IsAny<CancellationToken>()))
            .Callback<NavChangeSignal, CancellationToken>((s, _) =>
            {
                calls.Enqueue(s);
                if (calls.Select(c => c.Isin).Distinct().Count() == 2) bothFunds.TrySetResult(true);
            })
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();

        // Act
        _navSignals.OnNext(new NavChangeSignal(FundIsin, Date));
        _navSignals.OnNext(new NavChangeSignal(FundIsin, Date.AddDays(1)));
        _navSignals.OnNext(new NavChangeSignal(OtherIsin, Date));
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);
        await bothFunds.Task.WaitAsync(Timeout);
        // The duplicate would have been dispatched in the same burst as the other two.
        await Task.Delay(100);

        // Assert
        Assert.That(calls.Count(c => c.Isin == FundIsin), Is.EqualTo(1));
    }

    // ── Failure and cancellation ────────────────────────────────────────

    [Test]
    public async Task Start_EntryThrows_LaterSignalsStillArrive()
    {
        // Arrange
        var threw = NewCapture<bool>();
        var second = NewCapture<Step01DoneSignal>();
        _fixture.Freeze<Mock<IStep02MetricsCalculatorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()))
            .Returns<Step01DoneSignal, CancellationToken>((s, _) =>
            {
                if (s.Isin == FundIsin)
                {
                    threw.TrySetResult(true);
                    throw new InvalidOperationException("boom");
                }
                second.TrySetResult(s);
                return Task.CompletedTask;
            });
        using var sut = CreateStarted();
        var later = new Step01DoneSignal(OtherIsin, Date, RunId);

        // Act — the second signal is published only once the first has failed.
        _pipelineSignals.OnNext(new Step01DoneSignal(FundIsin, Date, RunId));
        await threw.Task.WaitAsync(Timeout);
        _pipelineSignals.OnNext(later);

        // Assert
        Assert.That(await second.Task.WaitAsync(Timeout), Is.EqualTo(later));
    }

    [Test]
    public async Task CancelInFlight_EntryRunning_CancelsItsToken()
    {
        // Arrange
        var token = NewCapture<CancellationToken>();
        _fixture.Freeze<Mock<IStep02MetricsCalculatorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step01DoneSignal, CancellationToken>((_, ct) => token.TrySetResult(ct))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();
        _pipelineSignals.OnNext(new Step01DoneSignal(FundIsin, Date, RunId));
        var handed = await token.Task.WaitAsync(Timeout);

        // Act
        sut.CancelInFlight();

        // Assert
        Assert.That(handed.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task CancelInFlight_ThenNewSignal_GetsFreshToken()
    {
        // Arrange
        var token = NewCapture<CancellationToken>();
        _fixture.Freeze<Mock<IStep02MetricsCalculatorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()))
            .Callback<Step01DoneSignal, CancellationToken>((_, ct) => token.TrySetResult(ct))
            .Returns(Task.CompletedTask);
        using var sut = CreateStarted();

        // Act
        sut.CancelInFlight();
        _pipelineSignals.OnNext(new Step01DoneSignal(FundIsin, Date, RunId));

        // Assert
        var handed = await token.Task.WaitAsync(Timeout);
        Assert.That(handed.IsCancellationRequested, Is.False);
    }

    // ── Daily timers ────────────────────────────────────────────────────

    [Test]
    public async Task Start_ClockReachesStep09Time_RunsStep09WithTimerTrigger()
    {
        // Arrange
        var got = NewCapture<Step09Trigger>();
        _fixture.Freeze<Mock<IStep09UniverseEnricherEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step09Trigger>(), It.IsAny<CancellationToken>()))
            .Callback<Step09Trigger, CancellationToken>((t, _) => got.TrySetResult(t))
            .ReturnsAsync(true);
        using var sut = CreateStarted();

        // Act
        Advance(UntilToday(LocalPipelineHost.Step09TimerAt));

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(Step09Trigger.Timer));
    }

    [Test]
    public async Task Start_ClockReachesStep10Time_RunsStep10WithTimerTrigger()
    {
        // Arrange
        var got = NewCapture<Step10Trigger>();
        _fixture.Freeze<Mock<IStep10PortfolioConstructorEntry>>()
            .Setup(e => e.HandleAsync(It.IsAny<Step10Trigger>(), It.IsAny<CancellationToken>()))
            .Callback<Step10Trigger, CancellationToken>((t, _) => got.TrySetResult(t))
            .ReturnsAsync(true);
        using var sut = CreateStarted();

        // Act
        Advance(UntilToday(LocalPipelineHost.Step10TimerAt));

        // Assert
        Assert.That(await got.Task.WaitAsync(Timeout), Is.EqualTo(Step10Trigger.Timer));
    }

    [Test]
    public async Task Start_TimerFired_RearmsForSameTimeTomorrow()
    {
        // Arrange
        using var runs = new SemaphoreSlim(0);
        _fixture.Freeze<Mock<IStep10PortfolioConstructorEntry>>()
            .Setup(e => e.HandleAsync(Step10Trigger.Timer, It.IsAny<CancellationToken>()))
            .Callback(() => runs.Release())
            .ReturnsAsync(true);
        using var sut = CreateStarted();
        Advance(UntilToday(LocalPipelineHost.Step10TimerAt));
        Assert.That(await runs.WaitAsync(Timeout), Is.True, "first day's run");

        // Act — a minute short of tomorrow's time, then the last minute.
        Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        var early = await runs.WaitAsync(TimeSpan.FromMilliseconds(100));
        Advance(TimeSpan.FromMinutes(1));
        var onTime = await runs.WaitAsync(Timeout);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(early, Is.False, "fired before tomorrow's time");
            Assert.That(onTime, Is.True, "did not fire at tomorrow's time");
        });
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    [Test]
    public void Start_Twice_Throws()
    {
        // Arrange
        using var sut = CreateStarted();

        // Act & Assert
        Assert.That(sut.Start, Throws.InvalidOperationException);
    }

    [Test]
    public void Dispose_Started_UnsubscribesFromBothBuses()
    {
        // Arrange
        var sut = CreateStarted();

        // Act
        sut.Dispose();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_navSignals.HasObservers, Is.False);
            Assert.That(_pipelineSignals.HasObservers, Is.False);
        });
    }
}
