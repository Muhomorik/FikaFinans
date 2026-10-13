using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(Step02MetricsCalculatorEntry))]
public sealed class Step02MetricsCalculatorEntryTests
{
    private static readonly Step01DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IStep02MetricsCalculator> _step02 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep02MetricsCalculator> AutoFixture builds for the entry
        // point hands back this mock on every call.
        _step02 = _fixture.Freeze<Mock<IStep02MetricsCalculator>>();

        _step02
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Step02MetricsCalculatorEntry CreateSut() => _fixture.Create<Step02MetricsCalculatorEntry>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_RunsPhasesInOrderAndEmitsLast()
    {
        // Arrange
        var calls = new List<string>();
        _step02.Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(true);
        _step02.Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step02.Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step02.Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((Domain.Funds.DataLoaderOutput)null!);
        _step02.Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);
        _step02.Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit")).Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert — no complete: a later step is the chain tail, so the run stays in flight for it.
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[]
                { "begin", "load", "assemble", "agent", "persist", "emit" }));
            _step02.Verify(x => x.FailAsync(
                It.IsAny<Step01DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task HandleAsync_StartRefused_RunsNothingElse()
    {
        // Arrange — a late signal.
        _step02
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _step02.Verify(x => x.LoadFundAsync(
                It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _step02.Verify(x => x.FailAsync(
                It.IsAny<Step01DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void HandleAsync_LoadThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — the "step 1 stored no record" case.
        var boom = new InvalidOperationException("no step 1 record");
        _step02
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(boom));
            _step02.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
            _step02.Verify(x => x.EmitDoneAsync(
                It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void HandleAsync_PersistThrows_FailsAndNeverEmits()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step02
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            _step02.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
            _step02.Verify(x => x.EmitDoneAsync(
                It.IsAny<Step01DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void HandleAsync_EmitThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — the last phase now that step 2 no longer completes the run.
        var boom = new InvalidOperationException("bus unreachable");
        _step02
            .Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(boom));
            _step02.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public void HandleAsync_Cancelled_DoesNotFail()
    {
        // Arrange
        _step02
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step02.Verify(x => x.FailAsync(
            It.IsAny<Step01DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoSignals_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep02MetricsCalculator>>(() =>
        {
            created++;
            return _step02.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Signal);
        await sut.HandleAsync(Signal);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
