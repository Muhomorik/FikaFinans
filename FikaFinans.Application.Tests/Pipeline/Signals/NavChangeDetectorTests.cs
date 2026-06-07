using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Signals;

[TestFixture]
[TestOf(typeof(NavChangeDetector))]
public sealed class NavChangeDetectorTests
{
    private const string Partition = "isin-progress";
    private static readonly DateTimeOffset June1 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June5 = new(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);

    private IFixture _fixture = null!;
    private Mock<ILatestNavProvider> _provider = null!;
    private Mock<IIsinProgressRepository> _isinProgress = null!;
    private Mock<INavSignalPublisher> _publisher = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _provider = _fixture.Freeze<Mock<ILatestNavProvider>>();
        _isinProgress = _fixture.Freeze<Mock<IIsinProgressRepository>>();
        _publisher = _fixture.Freeze<Mock<INavSignalPublisher>>();
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });

        // Default: no progress rows (every candidate is "never processed").
        SetupProgress();
    }

    private NavChangeDetector CreateSut() => _fixture.Create<NavChangeDetector>();

    private void SetupProvider(params FundNavInfo[] infos) =>
        _provider
            .Setup(x => x.GetLatestNavDatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(infos);

    private void SetupProgress(params IsinProgressEntity[] rows) =>
        _isinProgress
            .Setup(x => x.QueryPartitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static IsinProgressEntity Row(
        string isin, DateTimeOffset? latest, IsinProgressState state = IsinProgressState.Free) => new()
    {
        PartitionKey = Partition,
        RowKey = isin,
        Isin = isin,
        State = state,
        LatestProcessedNavDate = latest,
    };

    [Test]
    public async Task DetectAsync_NavDateNewerThanAnchor_EmitsSignal()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", June1));

        var signals = await CreateSut().DetectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(signals, Has.Count.EqualTo(1));
            Assert.That(signals[0].Isin.Value, Is.EqualTo("LU0001"));
            Assert.That(signals[0].NavDate, Is.EqualTo(June5));
        });
    }

    [Test]
    public async Task DetectAsync_NavDateEqualToAnchor_EmitsNothing()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", June5)); // equal → not strictly newer

        var signals = await CreateSut().DetectAsync();

        Assert.That(signals, Is.Empty);
    }

    [Test]
    public async Task DetectAsync_NavDateOlderThanAnchor_EmitsNothing()
    {
        SetupProvider(new FundNavInfo("LU0001", June1, "TestCo"));
        SetupProgress(Row("LU0001", June5)); // incoming older than processed

        var signals = await CreateSut().DetectAsync();

        Assert.That(signals, Is.Empty);
    }

    [Test]
    public async Task DetectAsync_NoProgressRow_EmitsSignal()
    {
        SetupProvider(new FundNavInfo("LU0002", June5, "TestCo"));
        // default progress = empty → never processed

        var signals = await CreateSut().DetectAsync();

        Assert.That(signals.Select(s => s.Isin.Value), Is.EqualTo(new[] { "LU0002" }));
    }

    [Test]
    public async Task DetectAsync_NullAnchor_EmitsSignal()
    {
        SetupProvider(new FundNavInfo("LU0002", June5, "TestCo"));
        SetupProgress(Row("LU0002", latest: null)); // row exists but never completed

        var signals = await CreateSut().DetectAsync();

        Assert.That(signals.Select(s => s.Isin.Value), Is.EqualTo(new[] { "LU0002" }));
    }

    [Test]
    public async Task DetectAsync_DifferentCompany_Excluded()
    {
        SetupProvider(
            new FundNavInfo("LU0001", June5, "OtherCo"),
            new FundNavInfo("LU0003", June5, "TestCo"));

        var signals = await CreateSut().DetectAsync();

        Assert.That(signals.Select(s => s.Isin.Value), Is.EqualTo(new[] { "LU0003" }),
            "only funds from the configured company are considered");
    }

    [Test]
    public async Task DetectAndPublishAsync_PublishesDetectedSignals()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));

        var published = await CreateSut().DetectAndPublishAsync();

        Assert.That(published, Has.Count.EqualTo(1));
        _publisher.Verify(x => x.PublishAsync(
            It.Is<IReadOnlyList<NavChangeSignal>>(l => l.Count == 1 && l[0].Isin.Value == "LU0001"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DetectAndPublishAsync_NoSignals_DoesNotPublish()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", June5)); // up to date → nothing to publish

        var published = await CreateSut().DetectAndPublishAsync();

        Assert.That(published, Is.Empty);
        _publisher.Verify(x => x.PublishAsync(
            It.IsAny<IReadOnlyList<NavChangeSignal>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── DescribeAsync: display-side classification (every candidate row) ──────

    [Test]
    public async Task DescribeAsync_NoProgressRow_ClassifiesNew()
    {
        SetupProvider(new FundNavInfo("LU0002", June5, "TestCo", "Acme Global Equity"));
        // default progress = empty → never processed → first run

        var rows = await CreateSut().DescribeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Kind, Is.EqualTo(NavSyncStatusKind.New));
            Assert.That(rows[0].Isin.Value, Is.EqualTo("LU0002"));
            Assert.That(rows[0].Name, Is.EqualTo("Acme Global Equity"));
            Assert.That(rows[0].CompanyName, Is.EqualTo("TestCo"));
            Assert.That(rows[0].LatestNavDate, Is.EqualTo(June5));
            Assert.That(rows[0].LastProcessedNavDate, Is.Null);
        });
    }

    [Test]
    public async Task DescribeAsync_NullAnchor_ClassifiesNew()
    {
        SetupProvider(new FundNavInfo("LU0002", June5, "TestCo"));
        SetupProgress(Row("LU0002", latest: null)); // row exists but never completed

        var rows = await CreateSut().DescribeAsync();

        Assert.That(rows[0].Kind, Is.EqualTo(NavSyncStatusKind.New));
    }

    [Test]
    public async Task DescribeAsync_NavNewerThanAnchor_ClassifiesChanged()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", June1));

        var rows = await CreateSut().DescribeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Kind, Is.EqualTo(NavSyncStatusKind.Changed));
            Assert.That(rows[0].LastProcessedNavDate, Is.EqualTo(June1));
        });
    }

    [Test]
    public async Task DescribeAsync_NavEqualToAnchor_ClassifiesUpToDate()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        SetupProgress(Row("LU0001", June5)); // caught up

        var rows = await CreateSut().DescribeAsync();

        Assert.That(rows[0].Kind, Is.EqualTo(NavSyncStatusKind.UpToDate));
    }

    [Test]
    public async Task DescribeAsync_StateProcessing_ClassifiesProcessing()
    {
        SetupProvider(new FundNavInfo("LU0001", June5, "TestCo"));
        // In flight: still Processing, anchor older than YR — Processing wins the label.
        SetupProgress(Row("LU0001", June1, IsinProgressState.Processing));

        var rows = await CreateSut().DescribeAsync();

        Assert.That(rows[0].Kind, Is.EqualTo(NavSyncStatusKind.Processing));
    }

    [Test]
    public async Task DescribeAsync_DifferentCompany_Excluded()
    {
        SetupProvider(
            new FundNavInfo("LU0001", June5, "OtherCo"),
            new FundNavInfo("LU0003", June5, "TestCo"));

        var rows = await CreateSut().DescribeAsync();

        Assert.That(rows.Select(r => r.Isin.Value), Is.EqualTo(new[] { "LU0003" }),
            "company filter applies to the display grid too");
    }
}
