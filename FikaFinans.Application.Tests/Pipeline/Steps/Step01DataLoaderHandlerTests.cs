using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Identifiers;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step01DataLoaderHandler))]
public sealed class Step01DataLoaderHandlerTests
{
    private static readonly NavChangeSignal Signal =
        new(new Isin("LU0000000001"), new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero));

    private IFixture _fixture = null!;
    private Mock<IIsinProgressStore> _progress = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();

        _progress
            .Setup(x => x.StartStep01Async(Signal.Isin, Signal.NavDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
    }

    private Step01DataLoaderHandler CreateSut() => _fixture.Create<Step01DataLoaderHandler>();

    // ── FailAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task FailAsync_AfterStart_FailsStep01WithErrorMessage()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("persist exploded"));

        // Assert
        _progress.Verify(x => x.FailStep01Async(
            Signal.Isin, "persist exploded", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FailAsync_NotStarted_LeavesRowAlone()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("anything"));

        // Assert
        _progress.Verify(x => x.FailStep01Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StartRefused_LeavesRowAlone()
    {
        // Arrange
        _progress
            .Setup(x => x.StartStep01Async(Signal.Isin, Signal.NavDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("anything"));

        // Assert
        _progress.Verify(x => x.FailStep01Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StoreThrows_DoesNotThrow()
    {
        // Arrange
        _progress
            .Setup(x => x.FailStep01Async(Signal.Isin, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => sut.FailAsync(Signal, new InvalidOperationException("persist exploded")));
    }
}
