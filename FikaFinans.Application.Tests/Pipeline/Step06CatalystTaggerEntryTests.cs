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
[TestOf(typeof(Step06CatalystTaggerEntry))]
public sealed class Step06CatalystTaggerEntryTests
{
    private static readonly Step05DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IStep06CatalystTagger> _step06 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep06CatalystTagger> AutoFixture builds for the entry point
        // hands back this mock on every call.
        _step06 = _fixture.Freeze<Mock<IStep06CatalystTagger>>();

        _step06
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Step06CatalystTaggerEntry CreateSut() => _fixture.Create<Step06CatalystTaggerEntry>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_RunsPhasesInOrderAndCompletesLast()
    {
        // Arrange
        var calls = new List<string>();
        _step06.Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(true);
        _step06.Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step06.Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step06.Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((DataLoaderOutput)null!);
        _step06.Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);
        _step06.Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit")).Returns(Task.CompletedTask);
        _step06.Setup(x => x.CompleteAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("complete")).Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        Assert.That(calls, Is.EqualTo(new[]
            { "begin", "load", "assemble", "agent", "persist", "emit", "complete" }));
        _step06.Verify(x => x.FailAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_StartRefused_RunsNothingElse()
    {
        // Arrange — a late signal.
        _step06
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        _step06.Verify(x => x.LoadFundAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step06.Verify(x => x.CompleteAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step06.Verify(x => x.FailAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_LoadThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — no macro context stored up to the week.
        var boom = new InvalidOperationException("No macro context for 2026-W23 or earlier.");
        _step06
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step06.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step06.Verify(x => x.CompleteAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AgentThrows_FailsAndNeverPersists()
    {
        // Arrange
        var boom = new InvalidOperationException("agent exploded");
        _step06
            .Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step06.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step06.Verify(x => x.PersistAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_PersistThrows_FailsAndNeverEmits()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step06
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step06.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step06.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_NeitherCompletesNorFails()
    {
        // Arrange — a shutdown mid LLM call.
        _step06
            .Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step06.Verify(x => x.CompleteAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step06.Verify(x => x.FailAsync(
            It.IsAny<Step05DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoSignals_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep06CatalystTagger>>(() =>
        {
            created++;
            return _step06.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Signal);
        await sut.HandleAsync(Signal);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
