using System.Reactive.Linq;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Signals;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Signals;

[TestFixture]
[TestOf(typeof(LocalRxPipelineSignalBus))]
public sealed class LocalRxPipelineSignalBusTests
{
    private static readonly Isin FundIsin = new("LU0000000001");
    private static readonly DateTimeOffset Date = new(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new Fixture().Customize(new AutoMoqCustomization());

    [Test]
    public async Task PublishAsync_Step01Done_PushesSignalToSubscribers()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();
        var received = new List<IStepDoneSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);
        var signal = new Step01DoneSignal(FundIsin, Date, RunId);

        // Act
        await sut.PublishAsync(signal);

        // Assert
        Assert.That(received, Is.EqualTo(new IStepDoneSignal[] { signal }));
    }

    [Test]
    public async Task PublishAsync_Step02Done_PushesSignalToSubscribers()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();
        var received = new List<IStepDoneSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);
        var signal = new Step02DoneSignal(FundIsin, Date, RunId);

        // Act
        await sut.PublishAsync(signal);

        // Assert
        Assert.That(received, Is.EqualTo(new IStepDoneSignal[] { signal }));
    }

    [Test]
    public async Task PublishAsync_Step03Done_PushesSignalToSubscribers()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();
        var received = new List<IStepDoneSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);
        var signal = new Step03DoneSignal(FundIsin, Date, RunId);

        // Act
        await sut.PublishAsync(signal);

        // Assert
        Assert.That(received, Is.EqualTo(new IStepDoneSignal[] { signal }));
    }

    [Test]
    public async Task PublishAsync_Step04Done_PushesSignalToSubscribers()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();
        var received = new List<IStepDoneSignal>();
        using var sub = sut.Signals.Subscribe(received.Add);
        var signal = new Step04DoneSignal(FundIsin, Date, RunId);

        // Act
        await sut.PublishAsync(signal);

        // Assert
        Assert.That(received, Is.EqualTo(new IStepDoneSignal[] { signal }));
    }

    [Test]
    public async Task Signals_FilteredByType_KeepsStepsApart()
    {
        // Arrange — each step listens for its predecessor's signal on the same stream it
        // publishes its own to, so its own must never look like a trigger.
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();
        var step01 = new List<Step01DoneSignal>();
        var step02 = new List<Step02DoneSignal>();
        var step03 = new List<Step03DoneSignal>();
        var step04 = new List<Step04DoneSignal>();
        using var sub01 = sut.Signals.OfType<Step01DoneSignal>().Subscribe(step01.Add);
        using var sub02 = sut.Signals.OfType<Step02DoneSignal>().Subscribe(step02.Add);
        using var sub03 = sut.Signals.OfType<Step03DoneSignal>().Subscribe(step03.Add);
        using var sub04 = sut.Signals.OfType<Step04DoneSignal>().Subscribe(step04.Add);

        // Act
        await sut.PublishAsync(new Step01DoneSignal(FundIsin, Date, RunId));
        await sut.PublishAsync(new Step02DoneSignal(FundIsin, Date, RunId));
        await sut.PublishAsync(new Step03DoneSignal(FundIsin, Date, RunId));
        await sut.PublishAsync(new Step04DoneSignal(FundIsin, Date, RunId));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(step01, Has.Count.EqualTo(1));
            Assert.That(step02, Has.Count.EqualTo(1));
            Assert.That(step03, Has.Count.EqualTo(1));
            Assert.That(step04, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void PublishAsync_NullStep02Done_ThrowsArgumentNullException()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishAsync((Step02DoneSignal)null!));
    }

    [Test]
    public void PublishAsync_NullStep03Done_ThrowsArgumentNullException()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishAsync((Step03DoneSignal)null!));
    }

    [Test]
    public void PublishAsync_NullStep04Done_ThrowsArgumentNullException()
    {
        // Arrange
        using var sut = _fixture.Create<LocalRxPipelineSignalBus>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishAsync((Step04DoneSignal)null!));
    }
}
