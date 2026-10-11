using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Identifiers;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(PipelineFrontDoor))]
public sealed class PipelineFrontDoorTests
{
    private static readonly NavChangeSignal Signal =
        new(new Isin("LU0000000001"), new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero));

    private IFixture _fixture = null!;
    private Mock<IStep01DataLoader> _step01 = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Frozen, so the Func<IStep01DataLoader> AutoFixture builds for the front door
        // hands back this mock on every call.
        _step01 = _fixture.Freeze<Mock<IStep01DataLoader>>();

        _step01
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private PipelineFrontDoor CreateSut() => _fixture.Create<PipelineFrontDoor>();

    [Test]
    public async Task HandleAsync_AllPhasesSucceed_CompletesAfterEmit()
    {
        // Arrange
        var calls = new List<string>();
        _step01
            .Setup(x => x.EmitDoneAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("emit"))
            .Returns(Task.CompletedTask);
        _step01
            .Setup(x => x.CompleteAsync(Signal, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("complete"))
            .Returns(Task.CompletedTask);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { "emit", "complete" }));
            _step01.Verify(x => x.FailAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task HandleAsync_StartRefused_NeitherCompletesNorFails()
    {
        // Arrange
        _step01
            .Setup(x => x.BeginProcessingAsync(Signal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await CreateSut().HandleAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _step01.Verify(x => x.CompleteAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _step01.Verify(x => x.FailAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void HandleAsync_PhaseThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange
        var boom = new InvalidOperationException("persist exploded");
        _step01
            .Setup(x => x.PersistAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(boom));
            _step01.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
            _step01.Verify(x => x.EmitDoneAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _step01.Verify(x => x.CompleteAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void HandleAsync_CompleteThrows_FailsWithThatErrorAndRethrows()
    {
        // Arrange
        var boom = new InvalidOperationException("store unreachable");
        _step01
            .Setup(x => x.CompleteAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(boom));
            _step01.Verify(x => x.FailAsync(Signal, boom, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public void HandleAsync_Cancelled_NeitherCompletesNorFails()
    {
        // Arrange
        _step01
            .Setup(x => x.LoadFundAsync(Signal, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().HandleAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            _step01.Verify(x => x.CompleteAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _step01.Verify(x => x.FailAsync(
                It.IsAny<NavChangeSignal>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }
}
