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
[TestOf(typeof(Step05MacroAlignerEntry))]
public sealed class Step05MacroAlignerEntryTests
{
    private static readonly Step04DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IStep05MacroAligner> _step05 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep05MacroAligner> AutoFixture builds for the entry point
        // hands back this mock on every call.
        _step05 = _fixture.Freeze<Mock<IStep05MacroAligner>>();

        _step05
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Step05MacroAlignerEntry CreateSut() => _fixture.Create<Step05MacroAlignerEntry>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_RunsPhasesInOrderAndEmitsLast()
    {
        // Arrange
        var calls = new List<string>();
        _step05.Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(true);
        _step05.Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step05.Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step05.Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((DataLoaderOutput)null!);
        _step05.Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);
        _step05.Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit")).Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert — no complete: a later step is the chain tail, so the run stays in flight for it.
        Assert.That(calls, Is.EqualTo(new[]
            { "begin", "load", "assemble", "agent", "persist", "emit" }));
        _step05.Verify(x => x.FailAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_StartRefused_RunsNothingElse()
    {
        // Arrange — a late signal.
        _step05
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        _step05.Verify(x => x.LoadFundAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step05.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step05.Verify(x => x.FailAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_LoadThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — no macro context stored up to the week.
        var boom = new InvalidOperationException("No macro context for 2026-W23 or earlier.");
        _step05
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step05.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step05.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AgentThrows_FailsAndNeverPersists()
    {
        // Arrange
        var boom = new InvalidOperationException("agent exploded");
        _step05
            .Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step05.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step05.Verify(x => x.PersistAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_PersistThrows_FailsAndNeverEmits()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step05
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step05.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step05.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_NeitherEmitsNorFails()
    {
        // Arrange — a shutdown mid LLM call.
        _step05
            .Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step05.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step05.Verify(x => x.FailAsync(
            It.IsAny<Step04DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoSignals_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep05MacroAligner>>(() =>
        {
            created++;
            return _step05.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Signal);
        await sut.HandleAsync(Signal);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
