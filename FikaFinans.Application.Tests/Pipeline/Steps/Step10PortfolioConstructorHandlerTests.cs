using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step10PortfolioConstructorHandler))]
public sealed class Step10PortfolioConstructorHandlerTests
{
    // A Saturday in ISO week 46, at the local timer's hour.
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);
    private static readonly IsoWeek Week = IsoWeek.From("2026-W46");
    private static readonly PipelineRunId RunId = new("20261114-universe-7c1e0d42");

    private static readonly IReadOnlyList<Isin> Universe =
        [new Isin("LU0000000001"), new Isin("LU0000000002"), new Isin("LU0000000003")];

    private IFixture _fixture = null!;
    private FakeTimeProvider _clock = null!;
    private Mock<IStep09Barrier> _barrier = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IStep10PortfolioConfigProvider> _configProvider = null!;
    private Mock<IStep10PortfolioReader> _portfolioReader = null!;
    private Mock<IPortfolioConstructorAgent> _agent = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private IReadOnlyList<FundRecord> _step09Records = null!;
    private Step10Portfolio _portfolio = null!;
    private PortfolioConstructorConfig _config = null!;
    private TradesOutput _agentOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _barrier = _fixture.Freeze<Mock<IStep09Barrier>>();
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _configProvider = _fixture.Freeze<Mock<IStep10PortfolioConfigProvider>>();
        _portfolioReader = _fixture.Freeze<Mock<IStep10PortfolioReader>>();
        _agent = _fixture.Freeze<Mock<IPortfolioConstructorAgent>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        // Injected rather than frozen: TimeProvider is an abstract class, not an interface.
        _clock = new FakeTimeProvider(Now);
        _fixture.Inject<TimeProvider>(_clock);
        _fixture.Freeze<Mock<IPipelineRunIdFactory>>().Setup(x => x.NewUniverseRunId(Now)).Returns(RunId);

        // Two of the three funds have a step 9 record; the third is mid-run.
        _step09Records = [MakeFund("LU0000000001"), MakeFund("LU0000000002")];
        _portfolio = new Step10Portfolio(
            _step09Records,
            [new FrozenPosition { Name = "Old Fund", CurrentValueKr = 182m, Reason = "frozen — cannot trade" }],
            50_000m,
            []);
        _config = PortfolioConstructorConfig.Default;
        _agentOutput = MakeTrades();

        // The happy path; tests override the one piece they are about.
        _barrier
            .Setup(x => x.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step09BarrierState(Universe, [], []));
        _progress
            .Setup(x => x.ReadStep09OutputAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", _step09Records));
        _progress
            .Setup(x => x.SaveStep10Async(It.IsAny<PipelineRunId>(), It.IsAny<TradesOutput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IsinProgressEntity(), new IsinProgressEntity()]);
        _configProvider
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_config);
        _portfolioReader
            .Setup(x => x.ReadAsync(_step09Records, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_portfolio);
        _agent
            .Setup(x => x.RunInMemory(
                It.IsAny<IsoWeek>(), It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<IReadOnlyList<FrozenPosition>>(),
                It.IsAny<decimal>(), It.IsAny<PortfolioConstructorConfig>(), It.IsAny<string?>()))
            .Returns(_agentOutput);
    }

    private Step10PortfolioConstructorHandler CreateSut() => _fixture.Create<Step10PortfolioConstructorHandler>();

    /// <summary>Runs every phase up to, but not including, persist.</summary>
    private static async Task RunThroughAgentAsync(Step10PortfolioConstructorHandler sut)
    {
        await sut.BeginRunAsync();
        await sut.LoadUniverseAsync();
        await sut.AssembleAgentInputAsync();
        await sut.RunAgentAsync();
    }

    // ── BeginRunAsync ─────────────────────────────────────────────────

    [Test]
    public async Task BeginRunAsync_Always_ReturnsMintedRunIdAndReportsStarted()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var runId = await sut.BeginRunAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runId, Is.EqualTo(RunId));
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Started && e.Isin == null)), Times.Once);
        });
    }

    [Test]
    public async Task BeginRunAsync_Always_TouchesNoRow()
    {
        // Arrange — the funds' own runs keep their rows; a step 10 run holds none.
        var sut = CreateSut();

        // Act
        await sut.BeginRunAsync();

        // Assert
        _progress.VerifyNoOtherCalls();
    }

    // ── LoadUniverseAsync ─────────────────────────────────────────────

    [Test]
    public async Task LoadUniverseAsync_AfterBegin_ReadsStep9ForStep9sUniverseAndReturnsTheCount()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginRunAsync();

        // Act
        var loaded = await sut.LoadUniverseAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(2));
            _progress.Verify(x => x.ReadStep09OutputAsync(Universe, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task LoadUniverseAsync_NoStep9RecordAnywhere_ReturnsZero()
    {
        // Arrange — the store says null when no row has a step 9 record.
        _progress
            .Setup(x => x.ReadStep09OutputAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();
        await sut.BeginRunAsync();

        // Act
        var loaded = await sut.LoadUniverseAsync();

        // Assert — the caller stops here, so the run Begin reported started is closed now.
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Zero);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Succeeded
                && e.Message != null && e.Message.Contains("step 9 record"))), Times.Once);
        });
    }

    [Test]
    public async Task LoadUniverseAsync_Records_ReportsNothingYet()
    {
        // Arrange — with records to trade on, the run is done only once persisted.
        var sut = CreateSut();
        await sut.BeginRunAsync();

        // Act
        await sut.LoadUniverseAsync();

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind != StepEventKind.Started)), Times.Never);
    }

    [Test]
    public void LoadUniverseAsync_BeforeBegin_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadUniverseAsync());
    }

    [Test]
    public async Task LoadUniverseAsync_ReadThrows_ReportsFailedAndRethrows()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStep09OutputAsync(Universe, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("bad row"));
        var sut = CreateSut();
        await sut.BeginRunAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => sut.LoadUniverseAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Failed && e.Message == "bad row")), Times.Once);
    }

    // ── AssembleAgentInputAsync ───────────────────────────────────────

    [Test]
    public async Task AssembleAgentInputAsync_AfterLoad_ReadsConfigAndThePortfolioOverTheRecords()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginRunAsync();
        await sut.LoadUniverseAsync();

        // Act
        await sut.AssembleAgentInputAsync();

        // Assert
        Assert.Multiple(() =>
        {
            _configProvider.Verify(x => x.GetConfigAsync(It.IsAny<CancellationToken>()), Times.Once);
            _portfolioReader.Verify(x => x.ReadAsync(_step09Records, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public void AssembleAgentInputAsync_BeforeLoad_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.AssembleAgentInputAsync());
    }

    [Test]
    public async Task AssembleAgentInputAsync_ConfigThrows_ReportsFailedAndRethrows()
    {
        // Arrange — a broken edit of config-10-portfolio.json.
        _configProvider
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("not valid JSON"));
        var sut = CreateSut();
        await sut.BeginRunAsync();
        await sut.LoadUniverseAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => sut.AssembleAgentInputAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task AssembleAgentInputAsync_HoldingsThrow_ReportsFailedAndRethrows()
    {
        // Arrange
        _portfolioReader
            .Setup(x => x.ReadAsync(It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("positions store down"));
        var sut = CreateSut();
        await sut.BeginRunAsync();
        await sut.LoadUniverseAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.AssembleAgentInputAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── RunAgentAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_AfterAssemble_PassesWeekPortfolioAndConfigWithNoRegime()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await RunThroughAgentAsync(sut);

        // Assert — the regime stays null until step 10's page settles it.
        _agent.Verify(x => x.RunInMemory(
            Week, _portfolio.Funds, _portfolio.FrozenPositions, 50_000m, _config, null), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_ClockInANewWeek_StampsThatWeek()
    {
        // Arrange — two days on is Monday of ISO week 47.
        _clock.Advance(TimeSpan.FromDays(2));
        _fixture.Freeze<Mock<IPipelineRunIdFactory>>()
            .Setup(x => x.NewUniverseRunId(_clock.GetUtcNow())).Returns(RunId);
        var sut = CreateSut();

        // Act
        await RunThroughAgentAsync(sut);

        // Assert
        _agent.Verify(x => x.RunInMemory(
            IsoWeek.From("2026-W47"), It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<IReadOnlyList<FrozenPosition>>(),
            It.IsAny<decimal>(), It.IsAny<PortfolioConstructorConfig>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_BeforeAssemble_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginRunAsync();
        await sut.LoadUniverseAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync());
    }

    [Test]
    public async Task RunAgentAsync_AgentThrows_ReportsFailedAndRethrows()
    {
        // Arrange — config drift halts the agent.
        _agent
            .Setup(x => x.RunInMemory(
                It.IsAny<IsoWeek>(), It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<IReadOnlyList<FrozenPosition>>(),
                It.IsAny<decimal>(), It.IsAny<PortfolioConstructorConfig>(), It.IsAny<string?>()))
            .Throws(new InvalidDataException("config_version '9.9.9'"));
        var sut = CreateSut();
        await sut.BeginRunAsync();
        await sut.LoadUniverseAsync();
        await sut.AssembleAgentInputAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => sut.RunAgentAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── PersistAsync ──────────────────────────────────────────────────

    [Test]
    public async Task PersistAsync_AfterRun_SavesTheOutputUnderTheRunIdAndReportsSucceeded()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync();

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.SaveStep10Async(RunId, _agentOutput, It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Succeeded && e.Duration != null)),
                Times.Once);
        });
    }

    [Test]
    public async Task PersistAsync_SaveThrows_ReportsFailedAndRethrows()
    {
        // Arrange
        _progress
            .Setup(x => x.SaveStep10Async(It.IsAny<PipelineRunId>(), It.IsAny<TradesOutput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act & Assert
        Assert.ThrowsAsync<IOException>(() => sut.PersistAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.PortfolioConstructor && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task PersistAsync_Cancelled_RethrowsWithoutReportingFailed()
    {
        // Arrange
        _progress
            .Setup(x => x.SaveStep10Async(It.IsAny<PipelineRunId>(), It.IsAny<TradesOutput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(() => sut.PersistAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Never);
    }

    [Test]
    public async Task PersistAsync_BeforeRun_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginRunAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.PersistAsync());
    }

    // ── ReadOutputAsync ───────────────────────────────────────────────

    [Test]
    public async Task ReadOutputAsync_Always_ReturnsTheStoredRun()
    {
        // Arrange
        _progress.Setup(x => x.ReadStep10OutputAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_agentOutput);
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync();

        // Assert
        Assert.That(output, Is.SameAs(_agentOutput));
    }

    [Test]
    public async Task ReadOutputAsync_StoreThrows_ReturnsNull()
    {
        // Arrange — a read feeds a view; a failure costs a display, not a crash.
        _progress
            .Setup(x => x.ReadStep10OutputAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("bad slice"));
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync();

        // Assert
        Assert.That(output, Is.Null);
    }

    // ── ComputeOutputAsync ────────────────────────────────────────────

    [Test]
    public async Task ComputeOutputAsync_Records_RunsTheAgentAndStoresNothing()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var output = await sut.ComputeOutputAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(output, Is.SameAs(_agentOutput));
            _agent.Verify(x => x.RunInMemory(
                Week, _portfolio.Funds, _portfolio.FrozenPositions, 50_000m, _config, null), Times.Once);
            _progress.Verify(x => x.SaveStep10Async(
                It.IsAny<PipelineRunId>(), It.IsAny<TradesOutput>(), It.IsAny<CancellationToken>()), Times.Never);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    [Test]
    public void ComputeOutputAsync_NoStep9Records_ThrowsInvalidOperationException()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStep09OutputAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComputeOutputAsync());
    }

    private static FundRecord MakeFund(string isin) => new()
    {
        Isin = isin,
        Metadata = new FundMetadata
        {
            Isin = isin,
            Name = $"Test Fund {isin}",
            CompanyName = "TestCo",
            CurrencyCode = "SEK",
            Category = "Branschfond, Energi",
            FundType = "EQUITY_FUND",
            ManagedType = "ACTIVE",
            TotalFee = 1.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = Array.Empty<NavBucket>(),
    };

    private static TradesOutput MakeTrades() => new()
    {
        GeneratedAt = Now.ToString("o"),
        IsoWeek = Week.Value,
        ConfigVersion = "1.0.0",
        Trades =
        [
            new Trade
            {
                Isin = "LU0000000001", FundName = "Test Fund LU0000000001", TradeType = TradeType.Buy,
                TradeReason = "catalyst_entry_fresh", AmountKr = 10_000m,
                SourceRecommendation = Recommendation.CatalystEntry, SourceConviction = 0.78m, AuditNotes = [],
            },
        ],
        RejectedRecommendations = [],
        CapitalSummary = new CapitalSummary { CashPolicy = new CashPolicySummary { FloorPct = 0.05m } },
        ConstraintViolations =
        [
            new ConstraintViolation { ViolationType = "min_trade_dropout", Isin = "LU0000000002", Message = "dropped" },
        ],
    };
}
