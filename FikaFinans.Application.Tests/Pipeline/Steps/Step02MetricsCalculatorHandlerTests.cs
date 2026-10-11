using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step02MetricsCalculatorHandler))]
public sealed class Step02MetricsCalculatorHandlerTests
{
    private static readonly Step01DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IStep02MetricsConfigProvider> _config = null!;
    private Mock<IMetricsCalculatorAgent> _agent = null!;
    private Mock<IPipelineSignals> _signals = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private FundRecord _step01Record = null!;
    private DataLoaderOutput _agentOutput = null!;
    private MetricsCalculatorConfig _metricsConfig = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _config = _fixture.Freeze<Mock<IStep02MetricsConfigProvider>>();
        _agent = _fixture.Freeze<Mock<IMetricsCalculatorAgent>>();
        _signals = _fixture.Freeze<Mock<IPipelineSignals>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        _step01Record = MakeFund(Signal.Isin.Value);
        _agentOutput = MakeOutput(MakeFund(Signal.Isin.Value));
        _metricsConfig = new MetricsCalculatorConfig { StaleSnapshotWarnDays = 21 };

        // The happy path; tests override the one piece they are about.
        _progress
            .Setup(x => x.StartStep02Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.DataLoader, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [_step01Record]));
        _config
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_metricsConfig);
        _agent
            .Setup(x => x.RunInMemory(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<MetricsCalculatorConfig>()))
            .Returns(_agentOutput);
    }

    private Step02MetricsCalculatorHandler CreateSut() => _fixture.Create<Step02MetricsCalculatorHandler>();

    /// <summary>Runs every phase up to, but not including, the one under test.</summary>
    private static async Task RunThroughAgentAsync(Step02MetricsCalculatorHandler sut)
    {
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);
        await sut.AssembleAgentInputAsync(Signal);
        await sut.RunAgentAsync(Signal);
    }

    // ── BeginProcessingAsync ──────────────────────────────────────────

    [Test]
    public async Task BeginProcessingAsync_StartGranted_ReturnsTrueAndReportsStarted()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(begun, Is.True);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.MetricsCalculator && e.Kind == StepEventKind.Started)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_StartRefused_ReturnsFalseAndReportsNothing()
    {
        // Arrange — a late signal.
        _progress
            .Setup(x => x.StartStep02Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(begun, Is.False);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_StartThrows_ReturnsFalseAndReportsFailed()
    {
        // Arrange
        _progress
            .Setup(x => x.StartStep02Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(begun, Is.False);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.MetricsCalculator && e.Kind == StepEventKind.Failed)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_Always_NeverStartsStep01()
    {
        // Arrange — step 1's start clears the row, which would wipe step 2's input.
        var sut = CreateSut();

        // Act
        await sut.BeginProcessingAsync(Signal);

        // Assert
        _progress.Verify(x => x.StartStep01Async(
            It.IsAny<Isin>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── LoadFundAsync ─────────────────────────────────────────────────

    [Test]
    public async Task LoadFundAsync_NoStep01Output_ThrowsAndReportsFailed()
    {
        // Arrange — step 1's agent dropped the fund, leaving its column empty.
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.DataLoader, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.MetricsCalculator && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task LoadFundAsync_OutputLacksThisFund_Throws()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.DataLoader, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [MakeFund("LU0000000099")]));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));
    }

    // ── RunAgentAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_AfterLoadAndAssemble_RunsAgentOnStep01RecordAndConfig()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);
        await sut.AssembleAgentInputAsync(Signal);

        // Act
        var result = await sut.RunAgentAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(_agentOutput));
            _agent.Verify(x => x.RunInMemory(
                new Company("TestCo"),
                new IsoWeek("2026-W23"),
                Signal.RunId,
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step01Record),
                _metricsConfig), Times.Once);
        });
    }

    [Test]
    public void RunAgentAsync_BeforeLoad_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync(Signal));
    }

    // ── PersistAsync ──────────────────────────────────────────────────

    [Test]
    public async Task PersistAsync_AfterAgent_SavesAgentOutputToStep02Column()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync(Signal);

        // Assert
        _progress.Verify(x => x.SaveStepOutputAsync(
            Signal.Isin,
            StepId.MetricsCalculator,
            Signal.RunId,
            _agentOutput,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PersistAsync_BeforeAgent_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.PersistAsync(Signal));
    }

    // ── EmitDoneAsync ─────────────────────────────────────────────────

    [Test]
    public async Task EmitDoneAsync_AfterPersist_PublishesStep02DoneAndReportsSucceeded()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);
        await sut.PersistAsync(Signal);

        // Act
        await sut.EmitDoneAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _signals.Verify(x => x.PublishAsync(
                new Step02DoneSignal(Signal.Isin, Signal.NavDate, Signal.RunId),
                It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.MetricsCalculator && e.Kind == StepEventKind.Succeeded)), Times.Once);
        });
    }

    // ── CompleteAsync ─────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_AfterStart_CompletesStep02()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.CompleteAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.CompleteStep02Async(Signal.Isin, It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.CompleteStep01Async(It.IsAny<Isin>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public void CompleteAsync_NotStarted_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(Signal));
    }

    // ── FailAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task FailAsync_AfterStart_FailsStep02WithErrorMessage()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("no step 1 record"));

        // Assert
        _progress.Verify(x => x.FailStep02Async(
            Signal.Isin, "no step 1 record", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FailAsync_StartRefused_LeavesRowAlone()
    {
        // Arrange — the row belongs to a newer run; failing it would free that run's lock.
        _progress
            .Setup(x => x.StartStep02Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("anything"));

        // Assert
        _progress.Verify(x => x.FailStep02Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StoreThrows_DoesNotThrow()
    {
        // Arrange
        _progress
            .Setup(x => x.FailStep02Async(Signal.Isin, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => sut.FailAsync(Signal, new InvalidOperationException("boom")));
    }

    // ── ReadOutputAsync ───────────────────────────────────────────────

    [Test]
    public async Task ReadOutputAsync_ReadsStep02Column()
    {
        // Arrange
        var stored = new StepOutput("[]", _agentOutput.Funds);
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MetricsCalculator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync(Signal.RunId);

        // Assert
        Assert.That(output, Is.SameAs(stored));
    }

    [Test]
    public async Task ReadOutputAsync_StoreThrows_ReturnsNull()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MetricsCalculator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync(Signal.RunId);

        // Assert
        Assert.That(output, Is.Null);
    }

    private static DataLoaderOutput MakeOutput(params FundRecord[] funds) => new()
    {
        GeneratedAt = string.Empty,
        IsoWeek = "2026-W23",
        Family = "TestCo",
        RunId = Signal.RunId,
        ConfigVersion = "1.0.0",
        Funds = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        DataQuality = new DataQuality(),
    };

    private static FundRecord MakeFund(string isin) => new()
    {
        Isin = isin,
        Metadata = new FundMetadata
        {
            Isin = isin,
            Name = $"Fund {isin}",
            CompanyName = "TestCo",
            CurrencyCode = "SEK",
            Category = "Globalfond",
            FundType = "EQUITY_FUND",
            ManagedType = "ACTIVE",
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = Array.Empty<NavBucket>(),
    };
}
