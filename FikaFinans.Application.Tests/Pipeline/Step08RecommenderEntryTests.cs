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
[TestOf(typeof(Step08RecommenderEntry))]
public sealed class Step08RecommenderEntryTests
{
    private static readonly Step07DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IStep08Recommender> _step08 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep08Recommender> AutoFixture builds for the entry point
        // hands back this mock on every call.
        _step08 = _fixture.Freeze<Mock<IStep08Recommender>>();

        _step08
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Step08RecommenderEntry CreateSut() => _fixture.Create<Step08RecommenderEntry>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_RunsPhasesInOrderAndCompletesLast()
    {
        // Arrange
        var calls = new List<string>();
        _step08.Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(true);
        _step08.Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step08.Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step08.Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((DataLoaderOutput)null!);
        _step08.Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);
        _step08.Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit")).Returns(Task.CompletedTask);
        _step08.Setup(x => x.CompleteAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("complete")).Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        Assert.That(calls, Is.EqualTo(new[]
            { "begin", "load", "assemble", "agent", "persist", "emit", "complete" }));
        _step08.Verify(x => x.FailAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_StartRefused_RunsNothingElse()
    {
        // Arrange — a late signal.
        _step08
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        _step08.Verify(x => x.LoadFundAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step08.Verify(x => x.CompleteAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step08.Verify(x => x.FailAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_LoadThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — step 7 stored no record for the run.
        var boom = new InvalidOperationException("Step 7 stored no record for ISIN LU0000000001 in run run-1.");
        _step08
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step08.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step08.Verify(x => x.CompleteAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AgentThrows_FailsAndNeverPersists()
    {
        // Arrange
        var boom = new InvalidOperationException("agent exploded");
        _step08
            .Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step08.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step08.Verify(x => x.PersistAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_PersistThrows_FailsAndNeverEmits()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step08
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step08.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step08.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_NeitherCompletesNorFails()
    {
        // Arrange — a shutdown mid store read; the agent itself awaits nothing.
        _step08
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step08.Verify(x => x.CompleteAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step08.Verify(x => x.FailAsync(
            It.IsAny<Step07DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoSignals_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep08Recommender>>(() =>
        {
            created++;
            return _step08.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Signal);
        await sut.HandleAsync(Signal);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
