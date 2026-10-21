using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step09UniverseEnricherHandler))]
public sealed class Step09UniverseEnricherHandlerTests
{
    // A Wednesday in ISO week 43.
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 22, 30, 0, TimeSpan.Zero);
    private static readonly IsoWeek Week = IsoWeek.From("2026-W43");
    private static readonly PipelineRunId PassRunId = new("20261021-universe-3f9a2b17");

    private static readonly IReadOnlyList<Isin> Universe = [new Isin("LU0000000001"), new Isin("LU0000000002")];

    private IFixture _fixture = null!;
    private Mock<IStep09Barrier> _barrier = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IStep09ConvictionConfigProvider> _configProvider = null!;
    private Mock<IUniverseEnricherAgent> _agent = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private IReadOnlyList<FundRecord> _step08Records = null!;
    private UniverseEnricherConfig _config = null!;
    private DataLoaderOutput _agentOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _barrier = _fixture.Freeze<Mock<IStep09Barrier>>();
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _configProvider = _fixture.Freeze<Mock<IStep09ConvictionConfigProvider>>();
        _agent = _fixture.Freeze<Mock<IUniverseEnricherAgent>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        // Injected rather than frozen: TimeProvider is an abstract class, not an interface.
        var clock = new Mock<TimeProvider>();
        clock.Setup(x => x.GetUtcNow()).Returns(Now);
        _fixture.Inject(clock.Object);
        _fixture.Freeze<Mock<IPipelineRunIdFactory>>().Setup(x => x.NewUniverseRunId(Now)).Returns(PassRunId);

        _step08Records = [MakeFund("LU0000000001"), MakeFund("LU0000000002")];
        _config = UniverseEnricherConfig.Default;
        _agentOutput = MakeOutput(MakeFund("LU0000000001"), MakeFund("LU0000000002"));

        // The happy path; tests override the one piece they are about.
        _barrier
            .Setup(x => x.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step09BarrierState(Universe, [], []));
        _progress
            .Setup(x => x.ReadStep08RecordsAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_step08Records);
        _progress
            .Setup(x => x.SaveStep09Async(It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IsinProgressEntity(), new IsinProgressEntity()]);
        _configProvider
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_config);
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<UniverseEnricherConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_agentOutput);
    }

    private Step09UniverseEnricherHandler CreateSut() => _fixture.Create<Step09UniverseEnricherHandler>();

    /// <summary>Runs every phase up to, but not including, persist.</summary>
    private static async Task RunThroughAgentAsync(Step09UniverseEnricherHandler sut)
    {
        await sut.BeginPassAsync(Universe);
        await sut.LoadUniverseAsync();
        await sut.AssembleAgentInputAsync();
        await sut.RunAgentAsync();
    }

    // ── BeginPassAsync ────────────────────────────────────────────────

    [Test]
    public async Task BeginPassAsync_Universe_ReturnsMintedRunIdAndReportsStarted()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var runId = await sut.BeginPassAsync(Universe);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runId, Is.EqualTo(PassRunId));
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.UniverseEnricher && e.Kind == StepEventKind.Started && e.Isin == null)), Times.Once);
        });
    }

    [Test]
    public async Task BeginPassAsync_Always_TouchesNoRow()
    {
        // Arrange — the funds' own runs keep their rows; a pass holds none.
        var sut = CreateSut();

        // Act
        await sut.BeginPassAsync(Universe);

        // Assert
        _progress.VerifyNoOtherCalls();
    }

    // ── LoadUniverseAsync ─────────────────────────────────────────────

    [Test]
    public async Task LoadUniverseAsync_AfterBegin_ReadsStep08RecordsForTheUniverse()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);

        // Act
        await sut.LoadUniverseAsync();

        // Assert
        _progress.Verify(x => x.ReadStep08RecordsAsync(Universe, It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(x => x.ReadStep08RecordsAsync(Universe, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadUniverseAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Kind == StepEventKind.Failed && e.Message == "store unreachable")), Times.Once);
    }

    // ── AssembleAgentInputAsync ───────────────────────────────────────

    [Test]
    public async Task AssembleAgentInputAsync_ConfigThrows_ReportsFailedAndRethrows()
    {
        // Arrange — a malformed config file.
        _configProvider
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("not valid JSON"));
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);
        await sut.LoadUniverseAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => sut.AssembleAgentInputAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── RunAgentAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_AfterAssemble_PassesCompanyWeekRunIdRecordsAndConfig()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await RunThroughAgentAsync(sut);

        // Assert — the pair ids carry the week the pass runs in.
        _agent.Verify(x => x.RunInMemoryAsync(
            new Company("TestCo"), Week, PassRunId, _step08Records, _config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_BeforeLoad_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync());
    }

    [Test]
    public async Task RunAgentAsync_BeforeAssemble_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);
        await sut.LoadUniverseAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync());
    }

    [Test]
    public async Task RunAgentAsync_AgentThrows_ReportsFailedAndRethrows()
    {
        // Arrange — the agent's weights check.
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<UniverseEnricherConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("weights sum to 0.9"));
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);
        await sut.LoadUniverseAsync();
        await sut.AssembleAgentInputAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => sut.RunAgentAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Kind == StepEventKind.Failed && e.Message == "weights sum to 0.9")), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_Cancelled_RethrowsWithoutReportingFailed()
    {
        // Arrange
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<UniverseEnricherConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);
        await sut.LoadUniverseAsync();
        await sut.AssembleAgentInputAsync();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(() => sut.RunAgentAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Never);
    }

    // ── PersistAsync ──────────────────────────────────────────────────

    [Test]
    public async Task PersistAsync_AfterRun_SavesTheEnrichedRecordsAndReportsSucceeded()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync();

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.SaveStep09Async(_agentOutput.Funds, It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.UniverseEnricher && e.Kind == StepEventKind.Succeeded && e.Duration != null)), Times.Once);
        });
    }

    [Test]
    public async Task PersistAsync_SaveThrows_ReportsFailedAndRethrows()
    {
        // Arrange
        _progress
            .Setup(x => x.SaveStep09Async(It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.PersistAsync());
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task PersistAsync_BeforeRun_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginPassAsync(Universe);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.PersistAsync());
    }

    // ── ReadOutputAsync ───────────────────────────────────────────────

    [Test]
    public async Task ReadOutputAsync_Always_ReadsStep9ForTheBarriersUniverse()
    {
        // Arrange
        var stored = new StepOutput("[]", _agentOutput.Funds);
        _progress
            .Setup(x => x.ReadStep09OutputAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync();

        // Assert
        Assert.That(output, Is.SameAs(stored));
    }

    [Test]
    public async Task ReadOutputAsync_StoreThrows_ReturnsNull()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStep09OutputAsync(It.IsAny<IReadOnlyList<Isin>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync();

        // Assert
        Assert.That(output, Is.Null);
    }

    // ── ComputeOutputAsync ────────────────────────────────────────────

    [Test]
    public async Task ComputeOutputAsync_BarrierOpen_StillRunsOverItsUniverseAndStoresNothing()
    {
        // Arrange — a debug look at the universe as it stands.
        _barrier
            .Setup(x => x.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step09BarrierState(
                Universe, [], [new Step09BarrierHold(Universe[1], NavSyncStatusKind.Processing)]));
        var sut = CreateSut();

        // Act
        var output = await sut.ComputeOutputAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(output, Is.SameAs(_agentOutput));
            _agent.Verify(x => x.RunInMemoryAsync(
                new Company("TestCo"), Week, PassRunId, _step08Records, _config, It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.SaveStep09Async(
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    [Test]
    public void ComputeOutputAsync_NoStep8Records_ThrowsInvalidOperationException()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStep08RecordsAsync(Universe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FundRecord>());
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComputeOutputAsync());
    }

    // ────────────────────────── helpers ──────────────────────────

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

    private static DataLoaderOutput MakeOutput(params FundRecord[] funds) => new()
    {
        GeneratedAt     = Now.ToString("o"),
        IsoWeek         = Week.Value,
        Family          = "TestCo",
        RunId           = PassRunId,
        ConfigVersion   = "1.0.0",
        Funds           = funds,
        FrozenPositions = [],
        CashAvailableKr = 0m,
        DataQuality     = new DataQuality { Warnings = ["fund LU0000000001 differentiator LLM failed"] },
    };
}
