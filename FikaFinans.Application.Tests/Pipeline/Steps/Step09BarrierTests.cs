using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step09Barrier))]
public sealed class Step09BarrierTests
{
    private const string Partition = "isin-progress";
    private static readonly DateTimeOffset June1 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June5 = new(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);

    private IFixture _fixture = null!;
    private Mock<ILatestNavProvider> _provider = null!;
    private Mock<IIsinProgressRepository> _isinProgress = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _provider = _fixture.Freeze<Mock<ILatestNavProvider>>();
        _isinProgress = _fixture.Freeze<Mock<IIsinProgressRepository>>();
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });

        SetupProvider();
        SetupProgress();
    }

    private Step09Barrier CreateSut() => _fixture.Create<Step09Barrier>();

    private void SetupProvider(params FundNavInfo[] infos) =>
        _provider
            .Setup(x => x.GetLatestNavDatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(infos);

    private void SetupProgress(params IsinProgressEntity[] rows) =>
        _isinProgress
            .Setup(x => x.QueryPartitionAsync(Partition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static IsinProgressEntity Row(
        string isin,
        DateTimeOffset? anchor,
        DateTimeOffset? navDate = null,
        IsinProgressState state = IsinProgressState.Free,
        string? lastError = null) => new()
    {
        PartitionKey = Partition,
        RowKey = isin,
        Isin = isin,
        State = state,
        NavDate = navDate ?? anchor,
        LatestProcessedNavDate = anchor,
        LastError = lastError,
    };

    [Test]
    public async Task CheckAsync_EveryFundUpToDate_IsClosed()
    {
        // Arrange
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"), new FundNavInfo("LU0002", June5, "TestCo"));
        SetupProgress(Row("LU0001", June5), Row("LU0002", June5));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.True);
            Assert.That(state.Universe.Select(i => i.Value), Is.EqualTo(new[] { "LU0001", "LU0002" }));
            Assert.That(state.Failed, Is.Empty);
            Assert.That(state.Holding, Is.Empty);
        });
    }

    [Test]
    public async Task CheckAsync_FundInFlight_HoldsItOpen()
    {
        // Arrange
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"), new FundNavInfo("LU0002", June5, "TestCo"));
        SetupProgress(Row("LU0001", June5), Row("LU0002", June1, navDate: June5, state: IsinProgressState.Processing));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.False);
            Assert.That(state.Holding, Is.EqualTo(new[]
            {
                new Step09BarrierHold("LU0002", NavSyncStatusKind.Processing),
            }));
        });
    }

    [Test]
    public async Task CheckAsync_FundWithoutRow_HoldsItOpenAsNew()
    {
        // Arrange
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.That(state.Holding, Is.EqualTo(new[] { new Step09BarrierHold("LU0001", NavSyncStatusKind.New) }));
    }

    [Test]
    public async Task CheckAsync_NavNewerThanAnchorNoFailure_HoldsItOpenAsChanged()
    {
        // Arrange — the NAV sync has not picked the new trading date up yet.
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", June1));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.That(state.Holding, Is.EqualTo(new[] { new Step09BarrierHold("LU0001", NavSyncStatusKind.Changed) }));
    }

    [Test]
    public async Task CheckAsync_FundFailedOnLatestTradingDate_ClosesAndListsItAsFailed()
    {
        // Arrange — a fail keeps the anchor, so the status reads Changed; the row's trading
        // date and error say the run is over.
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"), new FundNavInfo("LU0002", June5, "TestCo"));
        SetupProgress(Row("LU0001", June5), Row("LU0002", June1, navDate: June5, lastError: "step 4 failed"));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.True);
            Assert.That(state.Failed.Select(i => i.Value), Is.EqualTo(new[] { "LU0002" }));
            Assert.That(state.Universe, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task CheckAsync_FirstRunFailed_ClosesAndListsItAsFailed()
    {
        // Arrange — no anchor yet, so the status reads New; still a finished run.
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", anchor: null, navDate: June5, lastError: "step 1 failed"));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.True);
            Assert.That(state.Failed.Select(i => i.Value), Is.EqualTo(new[] { "LU0001" }));
        });
    }

    [Test]
    public async Task CheckAsync_FailedOnOlderTradingDate_HoldsItOpen()
    {
        // Arrange — the failure was for June 1; June 5 has not been tried yet.
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", anchor: null, navDate: June1, lastError: "step 4 failed"));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.False);
            Assert.That(state.Failed, Is.Empty);
        });
    }

    [Test]
    public async Task CheckAsync_FundOfOtherCompany_LeftOutOfUniverse()
    {
        // Arrange — matched case-insensitively, as the NAV sync matches it.
        SetupProvider(new FundNavInfo("LU0001", June5, "testco"), new FundNavInfo("LU0009", June5, "OtherCo"));
        SetupProgress(Row("LU0001", June5));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.True);
            Assert.That(state.Universe.Select(i => i.Value), Is.EqualTo(new[] { "LU0001" }));
        });
    }

    [Test]
    public async Task CheckAsync_EmptyCompanyFilter_TakesEveryFund()
    {
        // Arrange
        _fixture.Inject(new NavSyncOptions { CompanyFilter = string.Empty });
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"), new FundNavInfo("LU0009", June5, "OtherCo"));
        SetupProgress(Row("LU0001", June5), Row("LU0009", June5));

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.That(state.Universe, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CheckAsync_NoFunds_IsClosedOverAnEmptyUniverse()
    {
        // Arrange — nothing configured yet: the provider returns no funds.

        // Act
        var state = await CreateSut().CheckAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.IsClosed, Is.True);
            Assert.That(state.Universe, Is.Empty);
        });
    }
}
