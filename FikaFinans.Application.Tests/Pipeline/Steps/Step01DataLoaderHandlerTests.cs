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

    // ── FailOrphanedRunsAsync ─────────────────────────────────────────

    private static readonly DateTimeOffset SessionStart = new(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FailOrphanedRunsAsync_TwoOrphaned_FailsEachAtStep1AndReturnsCount()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep01Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000011"), Row("LU0000000012")]);
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(2));
            _progress.Verify(x => x.FailStep01Async(
                new Isin("LU0000000011"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.FailStep01Async(
                new Isin("LU0000000012"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_NoneOrphaned_ReturnsZeroAndFailsNothing()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep01Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IsinProgressEntity>());
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.Zero);
            _progress.Verify(x => x.FailStep01Async(
                It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_OneFailThrows_CarriesOnWithTheRest()
    {
        // Arrange — one bad row must not leave the others locked.
        _progress
            .Setup(x => x.ListOrphanedStep01Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000011"), Row("LU0000000012")]);
        _progress
            .Setup(x => x.FailStep01Async(new Isin("LU0000000011"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(1));
            _progress.Verify(x => x.FailStep01Async(
                new Isin("LU0000000012"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    private static IsinProgressEntity Row(string isin) => new() { Isin = isin };
}
