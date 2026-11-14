using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(Step10PortfolioConstructorEntry))]
public sealed class Step10PortfolioConstructorEntryTests
{
    private static readonly PipelineRunId RunId = new("20261114-universe-7c1e0d42");

    private IFixture _fixture = null!;
    private Mock<IStep10PortfolioConstructor> _step10 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep10PortfolioConstructor> AutoFixture builds for the entry
        // point hands back this mock on every call.
        _step10 = _fixture.Freeze<Mock<IStep10PortfolioConstructor>>();

        _step10.Setup(x => x.BeginRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RunId);
        _step10.Setup(x => x.LoadUniverseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
    }

    private Step10PortfolioConstructorEntry CreateSut() => _fixture.Create<Step10PortfolioConstructorEntry>();

    [Test]
    public async Task HandleAsync_Records_RunsThePhasesInOrder()
    {
        // Arrange
        var calls = new List<string>();
        _step10.Setup(x => x.BeginRunAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("begin")).ReturnsAsync(RunId);
        _step10.Setup(x => x.LoadUniverseAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).ReturnsAsync(2);
        _step10.Setup(x => x.AssembleAgentInputAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assemble")).Returns(Task.CompletedTask);
        _step10.Setup(x => x.RunAgentAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("agent")).ReturnsAsync((TradesOutput)null!);
        _step10.Setup(x => x.PersistAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("persist")).Returns(Task.CompletedTask);

        // Act
        var ran = await CreateSut().HandleAsync(Step10Trigger.Step09Done);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "begin", "load", "assemble", "agent", "persist" }));
        });
    }

    [Test]
    public async Task HandleAsync_NoStep9Record_StopsAfterTheLoad()
    {
        // Arrange — a fresh store: no step 9 pass yet.
        _step10.Setup(x => x.LoadUniverseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        // Act
        var ran = await CreateSut().HandleAsync(Step10Trigger.Timer);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.False);
            _step10.Verify(x => x.AssembleAgentInputAsync(It.IsAny<CancellationToken>()), Times.Never);
            _step10.Verify(x => x.RunAgentAsync(It.IsAny<CancellationToken>()), Times.Never);
            _step10.Verify(x => x.PersistAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void HandleAsync_AgentThrows_RethrowsAndNeverPersists()
    {
        // Arrange — config drift, say.
        var boom = new InvalidDataException("config_version '9.9.9'");
        _step10.Setup(x => x.RunAgentAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().HandleAsync(Step10Trigger.Timer));

        // Assert
        Assert.That(thrown, Is.SameAs(boom));
        _step10.Verify(x => x.PersistAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_AssembleThrows_RethrowsAndRunsNoAgent()
    {
        // Arrange — the positions store down.
        _step10
            .Setup(x => x.AssembleAgentInputAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("positions store down"));

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Step10Trigger.Step09Done));

        // Assert
        _step10.Verify(x => x.RunAgentAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void HandleAsync_Cancelled_Rethrows()
    {
        // Arrange — a shutdown mid load.
        _step10
            .Setup(x => x.LoadUniverseAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Step10Trigger.Timer));
        _step10.Verify(x => x.PersistAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleAsync_TwoTriggers_UsesAFreshHandlerForEach()
    {
        // Arrange — the handler keeps one run's state in fields.
        var created = 0;
        _fixture.Inject<Func<IStep10PortfolioConstructor>>(() =>
        {
            created++;
            return _step10.Object;
        });
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(Step10Trigger.Step09Done);
        await sut.HandleAsync(Step10Trigger.Timer);

        // Assert
        Assert.That(created, Is.EqualTo(2));
    }
}
