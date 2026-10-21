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
[TestOf(typeof(Step09UniverseEnricherEntry))]
public sealed class Step09UniverseEnricherEntryTests
{
    private static readonly IReadOnlyList<Isin> Universe = [new Isin("LU0000000001"), new Isin("LU0000000002")];
    private static readonly PipelineRunId PassRunId = new("20261021-universe-3f9a2b17");

    private IFixture _fixture = null!;
    private Mock<IStep09Barrier> _barrier = null!;
    private Mock<IStep09UniverseEnricher> _step09 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _barrier = _fixture.Freeze<Mock<IStep09Barrier>>();

        // Frozen, so the Func<IStep09UniverseEnricher> AutoFixture builds for the entry point
        // hands back this mock on every call.
        _step09 = _fixture.Freeze<Mock<IStep09UniverseEnricher>>();

        SetupBarrier(new Step09BarrierState(Universe, [], []));
        _step09
            .Setup(x => x.BeginPassAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassRunId);
    }

    private Step09UniverseEnricherEntry CreateSut() => _fixture.Create<Step09UniverseEnricherEntry>();

    private void SetupBarrier(Step09BarrierState state) =>
        _barrier.Setup(x => x.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(state);

    [Test]
    public async Task HandleAsync_BarrierClosed_RunsThePassInOrderOverTheBarriersUniverse()
    {
        // Arrange
        var calls = new List<string>();
        _step09.Setup(x => x.BeginPassAsync(Universe, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(PassRunId);
        _step09.Setup(x => x.LoadUniverseAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step09.Setup(x => x.AssembleAgentInputAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step09.Setup(x => x.RunAgentAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((DataLoaderOutput)null!);
        _step09.Setup(x => x.PersistAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);

        // Act
        var ran = await CreateSut().HandleAsync(Step09Trigger.Step08Done);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "begin", "load", "assemble", "agent", "persist" }));
        });
    }

    [Test]
    public async Task HandleAsync_FailedFundsOnly_StillRunsThePass()
    {
        // Arrange — a failed fund counts as finished: the smaller-but-clean universe.
        SetupBarrier(new Step09BarrierState(Universe, [Universe[1]], []));

        // Act
        var ran = await CreateSut().HandleAsync(Step09Trigger.Timer);

        // Assert
        Assert.That(ran, Is.True);
        _step09.Verify(x => x.PersistAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleAsync_BarrierOpen_StartsNoPass()
    {
        // Arrange
        SetupBarrier(new Step09BarrierState(
            Universe, [], [new Step09BarrierHold(Universe[1], NavSyncStatusKind.Processing)]));
        var created = 0;
        _fixture.Inject<Func<IStep09UniverseEnricher>>(() =>
        {
            created++;
            return _step09.Object;
        });

        // Act
        var ran = await CreateSut().HandleAsync(Step09Trigger.Step08Done);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.False);
            Assert.That(created, Is.Zero);
        });
    }

    [Test]
    public async Task HandleAsync_EmptyUniverse_StartsNoPass()
    {
        // Arrange — closed over nothing: no YieldRaccoon funds configured yet.
        SetupBarrier(new Step09BarrierState([], [], []));

        // Act
        var ran = await CreateSut().HandleAsync(Step09Trigger.Timer);

        // Assert
        Assert.That(ran, Is.False);
        _step09.Verify(x => x.BeginPassAsync(It.IsAny<IReadOnlyList<Isin>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AgentThrows_RethrowsAndNeverPersists()
    {
        // Arrange — a bad weights edit, say.
        var boom = new InvalidDataException("weights sum to 0.9");
        _step09.Setup(x => x.RunAgentAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().HandleAsync(Step09Trigger.Timer));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step09.Verify(x => x.PersistAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_BarrierThrows_RethrowsAndStartsNoPass()
    {
        // Arrange
        _barrier
            .Setup(x => x.CheckAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("YieldRaccoon DB locked"));

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Step09Trigger.Timer));

        // Assert
        _step09.Verify(x => x.BeginPassAsync(It.IsAny<IReadOnlyList<Isin>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_Rethrows()
    {
        // Arrange — a shutdown mid LLM calls.
        _step09
            .Setup(x => x.RunAgentAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Step09Trigger.Timer));
        _step09.Verify(x => x.PersistAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoTriggers_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one pass's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep09UniverseEnricher>>(() =>
        {
            created++;
            return _step09.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Step09Trigger.Step08Done);
        await sut.HandleAsync(Step09Trigger.Timer);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
