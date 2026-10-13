using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(Step03MacroAnalystEntry))]
public sealed class Step03MacroAnalystEntryTests
{
    private static readonly Step02DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IStep03MacroAnalyst> _step03 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep03MacroAnalyst> AutoFixture builds for the entry point
        // hands back this mock on every call.
        _step03 = _fixture.Freeze<Mock<IStep03MacroAnalyst>>();

        _step03
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Step03MacroAnalystEntry CreateSut() => _fixture.Create<Step03MacroAnalystEntry>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_RunsPhasesInOrderAndEmitsLast()
    {
        // Arrange
        var calls = new List<string>();
        _step03.Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(true);
        _step03.Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).Returns(Task.CompletedTask);
        _step03.Setup(x => x.AssembleAgentInputAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step03.Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((MacroContext)null!);
        _step03.Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);
        _step03.Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit")).Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert — no complete: step 4 is the chain tail, so the run stays in flight for it.
        Assert.That(calls, Is.EqualTo(new[]
            { "begin", "load", "assemble", "agent", "persist", "emit" }));
        _step03.Verify(x => x.FailAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_StartRefused_RunsNothingElse()
    {
        // Arrange — a late signal.
        _step03
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        _step03.Verify(x => x.LoadFundAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        _step03.Verify(x => x.FailAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_LoadThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange — the "no reports and nothing stored" case.
        var boom = new InvalidOperationException("No analytics reports for 2026-W23");
        _step03
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step03.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step03.Verify(x => x.PersistAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AgentThrows_FailsAndNeverPersists()
    {
        // Arrange — the LLM is down, or its answer stayed invalid after the retry.
        var boom = new InvalidOperationException("LLM returned invalid response after retry");
        _step03
            .Setup(x => x.RunAgentAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step03.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step03.Verify(x => x.PersistAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_PersistThrows_FailsAndNeverEmits()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step03
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step03.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        _step03.Verify(x => x.EmitDoneAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_DoesNotFail()
    {
        // Arrange
        _step03
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        _step03.Verify(x => x.FailAsync(
            It.IsAny<Step02DoneSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoSignals_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep03MacroAnalyst>>(() =>
        {
            created++;
            return _step03.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Signal);
        await sut.HandleAsync(Signal);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
