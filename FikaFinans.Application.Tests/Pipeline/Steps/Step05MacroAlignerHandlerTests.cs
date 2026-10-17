using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Macro;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step05MacroAlignerHandler))]
public sealed class Step05MacroAlignerHandlerTests
{
    private static readonly Step04DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    // The trading date's week.
    private static readonly IsoWeek Week = IsoWeek.From("2026-W23");

    private IFixture _fixture = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IMacroContextStore> _contexts = null!;
    private Mock<IMacroAlignerAgent> _agent = null!;
    private Mock<IPipelineSignals> _signals = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private FundRecord _step04Record = null!;
    private MacroContext _context = null!;
    private DataLoaderOutput _agentOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _contexts = _fixture.Freeze<Mock<IMacroContextStore>>();
        _agent = _fixture.Freeze<Mock<IMacroAlignerAgent>>();
        _signals = _fixture.Freeze<Mock<IPipelineSignals>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        _step04Record = MakeFund(Signal.Isin.Value);
        _context = MakeContext("2026-W23");
        _agentOutput = MakeOutput(MakeFund(Signal.Isin.Value));

        // The happy path; tests override the one piece they are about.
        _progress
            .Setup(x => x.StartStep05Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.SignalScorer, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [_step04Record]));
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_context);
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_agentOutput);
    }

    private Step05MacroAlignerHandler CreateSut() => _fixture.Create<Step05MacroAlignerHandler>();

    /// <summary>Runs every phase up to, but not including, the one under test.</summary>
    private static async Task RunThroughAgentAsync(Step05MacroAlignerHandler sut)
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
                e.Step == StepId.MacroAligner && e.Kind == StepEventKind.Started)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_StartRefused_ReturnsFalseAndReportsNothing()
    {
        // Arrange — a late signal.
        _progress
            .Setup(x => x.StartStep05Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.StartStep05Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(begun, Is.False);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.MacroAligner && e.Kind == StepEventKind.Failed)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_Always_NeverStartsStep01()
    {
        // Arrange — step 1's start clears the row, which would wipe step 5's input.
        var sut = CreateSut();

        // Act
        await sut.BeginProcessingAsync(Signal);

        // Assert
        _progress.Verify(x => x.StartStep01Async(
            It.IsAny<Isin>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── LoadFundAsync ─────────────────────────────────────────────────

    [Test]
    public async Task LoadFundAsync_NoStep04Output_ThrowsAndReportsFailed()
    {
        // Arrange — step 4 stored no record, leaving its column empty.
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.SignalScorer, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.MacroAligner && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task LoadFundAsync_OutputLacksThisFund_Throws()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.SignalScorer, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [MakeFund("LU0000000099")]));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));
    }

    [Test]
    public async Task LoadFundAsync_Always_ReadsStep04ColumnNotStep03()
    {
        // Arrange — every step reads its predecessor's column.
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.LoadFundAsync(Signal);

        // Assert
        _progress.Verify(x => x.ReadStepOutputAsync(
            StepId.MacroAnalyst, It.IsAny<PipelineRunId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task LoadFundAsync_NoContextUpToTheWeek_ThrowsAndReportsFailed()
    {
        // Arrange — aligning against no themes would look like a real "no theme", so it fails.
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MacroContext?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("2026-W23"));
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.MacroAligner && e.Kind == StepEventKind.Failed)), Times.Once);
        });
    }

    [Test]
    public async Task LoadFundAsync_Always_ReadsNewestContextForTheTradingDatesWeek()
    {
        // Arrange — the same read step 3's tab makes, never a lookup by report run id.
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.LoadFundAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _contexts.Verify(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()), Times.Once);
            _contexts.Verify(x => x.GetAsync(
                It.IsAny<FikaFinans.Domain.KanelBulleKapital.WeeklySummaryRunId>(),
                It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    // ── RunAgentAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_AfterLoad_RunsAgentOnStep04RecordAndContextThemes()
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
            _agent.Verify(x => x.RunInMemoryAsync(
                new Company("TestCo"),
                Week,
                Signal.RunId,
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step04Record),
                _context.RotationThemes,
                It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task RunAgentAsync_AgentWarns_StillSucceeds()
    {
        // Arrange — a bad LLM answer is a warning with alignment None, not a failure.
        var warned = MakeOutput(
            new DataQuality { Warnings = ["fund LU0000000001 LLM returned unknown theme_id 'x'"] },
            MakeFund(Signal.Isin.Value));
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(warned);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);

        // Act
        var result = await sut.RunAgentAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(warned));
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Never);
        });
    }

    [Test]
    public async Task RunAgentAsync_AgentThrows_ThrowsAndReportsFailed()
    {
        // Arrange
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>(), It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.MacroAligner && e.Kind == StepEventKind.Failed)), Times.Once);
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
    public async Task PersistAsync_AfterAgent_SavesAgentOutputToStep05Column()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync(Signal);

        // Assert
        _progress.Verify(x => x.SaveStepOutputAsync(
            Signal.Isin,
            StepId.MacroAligner,
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
    public async Task EmitDoneAsync_AfterPersist_PublishesStep05DoneAndReportsSucceeded()
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
                new Step05DoneSignal(Signal.Isin, Signal.NavDate, Signal.RunId),
                It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.MacroAligner && e.Kind == StepEventKind.Succeeded)), Times.Once);
        });
    }

    // ── CompleteAsync ─────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_AfterStart_CompletesStep05()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.CompleteAsync(Signal);

        // Assert
        _progress.Verify(x => x.CompleteStep05Async(Signal.Isin, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void CompleteAsync_BeforeStart_ThrowsInvalidOperationException()
    {
        // Arrange — completing a row this run never held would free another run's fund.
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(Signal));
    }

    [Test]
    public async Task CompleteAsync_StoreThrows_Rethrows()
    {
        // Arrange — rethrown so the caller fails the run instead of leaving it in flight.
        _progress
            .Setup(x => x.CompleteStep05Async(Signal.Isin, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(Signal));
    }

    // ── FailAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task FailAsync_AfterStart_FailsStep05WithErrorMessage()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("no macro context"));

        // Assert
        _progress.Verify(x => x.FailStep05Async(
            Signal.Isin, "no macro context", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FailAsync_StartRefused_LeavesRowAlone()
    {
        // Arrange — the row belongs to a newer run; failing it would free that run's lock.
        _progress
            .Setup(x => x.StartStep05Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("anything"));

        // Assert
        _progress.Verify(x => x.FailStep05Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StoreThrows_DoesNotThrow()
    {
        // Arrange
        _progress
            .Setup(x => x.FailStep05Async(Signal.Isin, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => sut.FailAsync(Signal, new InvalidOperationException("boom")));
    }

    // ── ReadOutputAsync ───────────────────────────────────────────────

    [Test]
    public async Task ReadOutputAsync_ReadsStep05Column()
    {
        // Arrange
        var stored = new StepOutput("[]", _agentOutput.Funds);
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MacroAligner, Signal.RunId, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.ReadStepOutputAsync(StepId.MacroAligner, Signal.RunId, It.IsAny<CancellationToken>()))
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
    public async Task FailOrphanedRunsAsync_TwoOrphaned_FailsEachAtStep5AndReturnsCount()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep05Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000051"), Row("LU0000000052")]);
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(2));
            _progress.Verify(x => x.FailStep05Async(
                new Isin("LU0000000051"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.FailStep05Async(
                new Isin("LU0000000052"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_NoneOrphaned_ReturnsZeroAndFailsNothing()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep05Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IsinProgressEntity>());
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.Zero);
            _progress.Verify(x => x.FailStep05Async(
                It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_OneFailThrows_CarriesOnWithTheRest()
    {
        // Arrange — one bad row must not leave the others locked.
        _progress
            .Setup(x => x.ListOrphanedStep05Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000051"), Row("LU0000000052")]);
        _progress
            .Setup(x => x.FailStep05Async(new Isin("LU0000000051"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(1));
            _progress.Verify(x => x.FailStep05Async(
                new Isin("LU0000000052"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    // ── ComputeOutputAsync ────────────────────────────────────────────

    [Test]
    public async Task ComputeOutputAsync_Step04Stored_RunsAgentOnStep04RecordsAndContextThemes()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var output = await sut.ComputeOutputAsync(Signal.RunId, Week);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(output, Is.SameAs(_agentOutput));
            _agent.Verify(x => x.RunInMemoryAsync(
                new Company("TestCo"), Week, Signal.RunId,
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step04Record),
                _context.RotationThemes,
                It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public void ComputeOutputAsync_NoStep04Output_ThrowsInvalidOperationException()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.SignalScorer, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComputeOutputAsync(Signal.RunId, Week));
    }

    [Test]
    public void ComputeOutputAsync_NoContext_ThrowsInvalidOperationException()
    {
        // Arrange
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MacroContext?)null);
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComputeOutputAsync(Signal.RunId, Week));
    }

    [Test]
    public async Task ComputeOutputAsync_Always_NeitherStartsNorStoresNorReports()
    {
        // Arrange — a debug run must not take the fund's lock or replace what step 6 reads.
        var sut = CreateSut();

        // Act
        await sut.ComputeOutputAsync(Signal.RunId, Week);

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.StartStep05Async(
                It.IsAny<Isin>(), It.IsAny<PipelineRunId>(), It.IsAny<CancellationToken>()), Times.Never);
            _progress.Verify(x => x.SaveStepOutputAsync(
                It.IsAny<Isin>(), It.IsAny<StepId>(), It.IsAny<PipelineRunId>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<CancellationToken>()), Times.Never);
            _signals.Verify(x => x.PublishAsync(
                It.IsAny<Step05DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    private static IsinProgressEntity Row(string isin) => new() { Isin = isin };

    private static MacroContext MakeContext(string week) => new()
    {
        GeneratedAt = "2026-06-05T10:00:00.0000000+00:00",
        IsoWeek = week,
        ConfigVersion = "1.0.0",
        SourceRunIds = new SourceRunIds
        {
            WeeklySummaryRunId = "ws-1",
            SubstitutionChainRunId = "sc-1",
            RotationTargetsRunId = "rt-1",
        },
        MacroRegime = MacroRegime.Mixed,
        RegimeConfidence = 0.5m,
        NetMoodInput = MarketSentiment.Mixed,
        Catalysts = [],
        RotationThemes =
        [
            new RotationTheme
            {
                Id = "rot_theme_energy",
                Label = "Energy",
                SignalStrength = SignalStrength.Strong,
                AffectedCategories = ["Branschfond, Energi"],
                Rationale = "synthetic",
            },
        ],
    };

    private static DataLoaderOutput MakeOutput(params FundRecord[] funds)
        => MakeOutput(new DataQuality(), funds);

    private static DataLoaderOutput MakeOutput(DataQuality dataQuality, params FundRecord[] funds) => new()
    {
        GeneratedAt = string.Empty,
        IsoWeek = "2026-W23",
        Family = "TestCo",
        RunId = Signal.RunId,
        ConfigVersion = "1.0.0",
        Funds = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        DataQuality = dataQuality,
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
