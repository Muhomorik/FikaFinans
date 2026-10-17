using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(Step04SignalScorerEntry))]
public sealed class Step04SignalScorerEntryTests
{
    private static readonly Step03DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IStep04SignalScorer> _step04 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep04SignalScorer> AutoFixture builds for the entry point
        // hands back this mock on every call.
        _step04 = _fixture.Freeze<Mock<IStep04SignalScorer>>();

        _step04
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Step04SignalScorerEntry CreateSut() => _fixture.Create<Step04SignalScorerEntry>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_RunsPhasesInOrderAndEmitsLast()
    {
        // Arrange
        var calls = new List<string>();
        _step04.Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(true);
        _step04.Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step04.Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step04.Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((DataLoaderOutput)null!);
        _step04.Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);
        _step04.Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit")).Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert — no complete: step 5 is the chain tail, so the run stays in flight for it.
        Assert.That(calls, Is.EqualTo(new[]
            { "begin", "load", "assemble", "agent", "persist", "emit" }));
        _step04.Verify(x => x.FailAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_StartRefused_RunsNothingElse()
    {
        // Arrange — a late signal.
        _step04
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        _step04.Verify(x => x.LoadFundAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step04.Verify(x => x.FailAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_LoadThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — step 3 stored no record for the run.
        var boom = new InvalidOperationException("Step 3 stored no record");
        _step04
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step04.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step04.Verify(x => x.RunAgentAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AssembleThrows_FailsAndNeverRunsAgent()
    {
        // Arrange — a broken edit of the config file.
        var boom = new InvalidDataException("Step 4 config is not valid JSON");
        _step04
            .Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step04.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step04.Verify(x => x.RunAgentAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_PersistThrows_FailsAndNeverEmits()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step04
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step04.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step04.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_NeitherEmitsNorFails()
    {
        // Arrange
        _step04
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step04.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step04.Verify(x => x.FailAsync(
            It.IsAny<Step03DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoSignals_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep04SignalScorer>>(() =>
        {
            created++;
            return _step04.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Signal);
        await sut.HandleAsync(Signal);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
