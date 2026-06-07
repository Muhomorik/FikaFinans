using System.Reactive.Linq;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Infrastructure.Pipeline.Signals;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Signals;

[TestFixture]
[TestOf(typeof(LocalRxNavSignalBus))]
public sealed class LocalRxNavSignalBusTests
{
    private static readonly DateTimeOffset Date = new(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new Fixture().Customize(new AutoMoqCustomization());

    private static NavChangeSignal Signal(string isin) => new(new Isin(isin), Date);

    [Test]
    public async Task PublishAsync_PushesEachSignalToSubscribersInOrder()
    {
        using var sut = _fixture.Create<LocalRxNavSignalBus>();
        var received = new List<NavChangeSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);

        await sut.PublishAsync(new[] { Signal("LU0001"), Signal("LU0002") });

        Assert.That(received.Select(s => s.Isin.Value), Is.EqualTo(new[] { "LU0001", "LU0002" }));
    }

    [Test]
    public async Task PublishAsync_MultipleSubscribers_AllReceive()
    {
        using var sut = _fixture.Create<LocalRxNavSignalBus>();
        var a = new List<NavChangeSignal>();
        var b = new List<NavChangeSignal>();
        using var subA = sut.Signals.Subscribe(a.Add);
        using var subB = sut.Signals.Subscribe(b.Add);

        await sut.PublishAsync(new[] { Signal("LU0001") });

        Assert.Multiple(() =>
        {
            Assert.That(a, Has.Count.EqualTo(1));
            Assert.That(b, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Signals_LateSubscriber_DoesNotReceivePriorSignals()
    {
        using var sut = _fixture.Create<LocalRxNavSignalBus>();
        await sut.PublishAsync(new[] { Signal("LU0001") }); // before any subscriber

        var received = new List<NavChangeSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);
        await sut.PublishAsync(new[] { Signal("LU0002") });

        Assert.That(received.Select(s => s.Isin.Value), Is.EqualTo(new[] { "LU0002" }),
            "hot stream — late subscriber only sees signals published after it subscribed");
    }

    [Test]
    public void PublishAsync_NullSignals_ThrowsArgumentNullException()
    {
        using var sut = _fixture.Create<LocalRxNavSignalBus>();

        Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishAsync(null!));
    }

    [Test]
    public async Task PublishAsync_EmptyList_NoEmissions()
    {
        using var sut = _fixture.Create<LocalRxNavSignalBus>();
        var received = new List<NavChangeSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);

        await sut.PublishAsync(Array.Empty<NavChangeSignal>());

        Assert.That(received, Is.Empty);
    }
}
