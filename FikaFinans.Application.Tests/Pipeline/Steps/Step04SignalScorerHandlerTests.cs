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
[TestOf(typeof(Step04SignalScorerHandler))]
public sealed class Step04SignalScorerHandlerTests
{
    private static readonly Step03DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private IFixture _fixture = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IStep04SignalsConfigProvider> _config = null!;
    private Mock<ISignalScorerAgent> _agent = null!;
    private Mock<IPipelineSignals> _signals = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private FundRecord _step03Record = null!;
    private DataLoaderOutput _agentOutput = null!;
    private SignalScorerConfig _signalsConfig = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _config = _fixture.Freeze<Mock<IStep04SignalsConfigProvider>>();
        _agent = _fixture.Freeze<Mock<ISignalScorerAgent>>();
        _signals = _fixture.Freeze<Mock<IPipelineSignals>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        _step03Record = MakeFund(Signal.Isin.Value);
        _agentOutput = MakeOutput(MakeFund(Signal.Isin.Value));
        _signalsConfig = new SignalScorerConfig
        {
            DataQualityHandling = new DataQualityHandling { TreatNanSharpeAsZero = false },
        };

        // The happy path; tests override the one piece they are about.
        _progress
            .Setup(x => x.StartStep04Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MacroAnalyst, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [_step03Record]));
        _config
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_signalsConfig);
        _agent
            .Setup(x => x.RunInMemory(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<SignalScorerConfig>()))
            .Returns(_agentOutput);
    }

    private Step04SignalScorerHandler CreateSut() => _fixture.Create<Step04SignalScorerHandler>();

    /// <summary>Runs every phase up to, but not including, the one under test.</summary>
    private static async Task RunThroughAgentAsync(Step04SignalScorerHandler sut)
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
                e.Step == StepId.SignalScorer && e.Kind == StepEventKind.Started)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_StartRefused_ReturnsFalseAndReportsNothing()
    {
        // Arrange — a late signal.
        _progress
            .Setup(x => x.StartStep04Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.StartStep04Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(begun, Is.False);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.SignalScorer && e.Kind == StepEventKind.Failed)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_Always_NeverStartsStep01()
    {
        // Arrange — step 1's start clears the row, which would wipe step 4's input.
        var sut = CreateSut();

        // Act
        await sut.BeginProcessingAsync(Signal);

        // Assert
        _progress.Verify(x => x.StartStep01Async(
            It.IsAny<Isin>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── LoadFundAsync ─────────────────────────────────────────────────

    [Test]
    public async Task LoadFundAsync_NoStep03Output_ThrowsAndReportsFailed()
    {
        // Arrange — step 3 stored no record, leaving its column empty.
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MacroAnalyst, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.SignalScorer && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task LoadFundAsync_OutputLacksThisFund_Throws()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MacroAnalyst, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [MakeFund("LU0000000099")]));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));
    }

    [Test]
    public async Task LoadFundAsync_Always_ReadsStep03ColumnNotStep02()
    {
        // Arrange — every step reads its predecessor's column.
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.LoadFundAsync(Signal);

        // Assert
        _progress.Verify(x => x.ReadStepOutputAsync(
            StepId.MetricsCalculator, It.IsAny<PipelineRunId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── AssembleAgentInputAsync ───────────────────────────────────────

    [Test]
    public async Task AssembleAgentInputAsync_ProviderThrows_ThrowsAndReportsFailed()
    {
        // Arrange — a broken config edit must not run on settings the user did not choose.
        _config
            .Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("not valid JSON"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidDataException>(() => sut.AssembleAgentInputAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.SignalScorer && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── RunAgentAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_AfterLoadAndAssemble_RunsAgentOnStep03RecordAndConfig()
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
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step03Record),
                _signalsConfig), Times.Once);
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
    public async Task PersistAsync_AfterAgent_SavesAgentOutputToStep04Column()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync(Signal);

        // Assert
        _progress.Verify(x => x.SaveStepOutputAsync(
            Signal.Isin,
            StepId.SignalScorer,
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
    public async Task EmitDoneAsync_AfterPersist_PublishesStep04DoneAndReportsSucceeded()
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
                new Step04DoneSignal(Signal.Isin, Signal.NavDate, Signal.RunId),
                It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.SignalScorer && e.Kind == StepEventKind.Succeeded)), Times.Once);
        });
    }

    // ── FailAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task FailAsync_AfterStart_FailsStep04WithErrorMessage()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("no step 3 record"));

        // Assert
        _progress.Verify(x => x.FailStep04Async(
            Signal.Isin, "no step 3 record", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FailAsync_StartRefused_LeavesRowAlone()
    {
        // Arrange — the row belongs to a newer run; failing it would free that run's lock.
        _progress
            .Setup(x => x.StartStep04Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("anything"));

        // Assert
        _progress.Verify(x => x.FailStep04Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StoreThrows_DoesNotThrow()
    {
        // Arrange
        _progress
            .Setup(x => x.FailStep04Async(Signal.Isin, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => sut.FailAsync(Signal, new InvalidOperationException("boom")));
    }

    // ── ReadOutputAsync ───────────────────────────────────────────────

    [Test]
    public async Task ReadOutputAsync_ReadsStep04Column()
    {
        // Arrange
        var stored = new StepOutput("[]", _agentOutput.Funds);
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.SignalScorer, Signal.RunId, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.ReadStepOutputAsync(StepId.SignalScorer, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var output = await sut.ReadOutputAsync(Signal.RunId);

        // Assert
        Assert.That(output, Is.Null);
    }

    // ── FailOrphanedRunsAsync ─────────────────────────────────────────

    private static readonly DateTimeOffset SessionStart = new(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FailOrphanedRunsAsync_TwoOrphaned_FailsEachAtStep4AndReturnsCount()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep04Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000041"), Row("LU0000000042")]);
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(2));
            _progress.Verify(x => x.FailStep04Async(
                new Isin("LU0000000041"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.FailStep04Async(
                new Isin("LU0000000042"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_NoneOrphaned_ReturnsZeroAndFailsNothing()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep04Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IsinProgressEntity>());
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.Zero);
            _progress.Verify(x => x.FailStep04Async(
                It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_OneFailThrows_CarriesOnWithTheRest()
    {
        // Arrange — one bad row must not leave the others locked.
        _progress
            .Setup(x => x.ListOrphanedStep04Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000041"), Row("LU0000000042")]);
        _progress
            .Setup(x => x.FailStep04Async(new Isin("LU0000000041"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(1));
            _progress.Verify(x => x.FailStep04Async(
                new Isin("LU0000000042"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    // ── ComputeOutputAsync ────────────────────────────────────────────

    private static readonly IsoWeek Week = IsoWeek.From("2026-W23");

    [Test]
    public async Task ComputeOutputAsync_Step03Stored_RunsAgentOnStep03RecordsAndConfig()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var output = await sut.ComputeOutputAsync(Signal.RunId, Week);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(output, Is.SameAs(_agentOutput));
            _agent.Verify(x => x.RunInMemory(
                new Company("TestCo"), Week, Signal.RunId,
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step03Record),
                _signalsConfig), Times.Once);
        });
    }

    [Test]
    public void ComputeOutputAsync_NoStep03Output_ThrowsInvalidOperationException()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MacroAnalyst, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComputeOutputAsync(Signal.RunId, Week));
    }

    [Test]
    public async Task ComputeOutputAsync_Always_NeitherStartsNorStoresNorReports()
    {
        // Arrange — a debug run must not take the fund's lock or replace what step 5 reads.
        var sut = CreateSut();

        // Act
        await sut.ComputeOutputAsync(Signal.RunId, Week);

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.StartStep04Async(
                It.IsAny<Isin>(), It.IsAny<PipelineRunId>(), It.IsAny<CancellationToken>()), Times.Never);
            _progress.Verify(x => x.SaveStepOutputAsync(
                It.IsAny<Isin>(), It.IsAny<StepId>(), It.IsAny<PipelineRunId>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<CancellationToken>()), Times.Never);
            _signals.Verify(x => x.PublishAsync(
                It.IsAny<Step04DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    private static IsinProgressEntity Row(string isin) => new() { Isin = isin };

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
