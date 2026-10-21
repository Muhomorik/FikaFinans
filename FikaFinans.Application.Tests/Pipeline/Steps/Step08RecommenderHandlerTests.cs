using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
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
[TestOf(typeof(Step08RecommenderHandler))]
public sealed class Step08RecommenderHandlerTests
{
    private static readonly Step07DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    // The trading date's week.
    private static readonly IsoWeek Week = IsoWeek.From("2026-W23");

    private IFixture _fixture = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IRecommenderAgent> _agent = null!;
    private Mock<IPipelineSignals> _signals = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private FundRecord _step07Record = null!;
    private DataLoaderOutput _agentOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _agent = _fixture.Freeze<Mock<IRecommenderAgent>>();
        _signals = _fixture.Freeze<Mock<IPipelineSignals>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        _step07Record = MakeFund(Signal.Isin.Value);
        _agentOutput = MakeOutput(MakeFund(Signal.Isin.Value));

        // The happy path; tests override the one piece they are about.
        _progress
            .Setup(x => x.StartStep08Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.ThesisValidator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [_step07Record]));
        _agent
            .Setup(x => x.RunInMemory(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>()))
            .Returns(_agentOutput);
        _progress
            .Setup(x => x.SaveStepOutputAsync(
                Signal.Isin, StepId.Recommender, Signal.RunId, It.IsAny<DataLoaderOutput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
    }

    private Step08RecommenderHandler CreateSut() => _fixture.Create<Step08RecommenderHandler>();

    /// <summary>Runs every phase up to, but not including, the one under test.</summary>
    private static async Task RunThroughAgentAsync(Step08RecommenderHandler sut)
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
                e.Step == StepId.Recommender && e.Kind == StepEventKind.Started)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_StartRefused_ReturnsFalseAndReportsNothing()
    {
        // Arrange — a late signal.
        _progress
            .Setup(x => x.StartStep08Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.StartStep08Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(begun, Is.False);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.Recommender && e.Kind == StepEventKind.Failed)), Times.Once);
        });
    }

    [Test]
    public async Task BeginProcessingAsync_Always_NeverStartsStep01()
    {
        // Arrange — step 1's start clears the row, which would wipe step 8's input.
        var sut = CreateSut();

        // Act
        await sut.BeginProcessingAsync(Signal);

        // Assert
        _progress.Verify(x => x.StartStep01Async(
            It.IsAny<Isin>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── LoadFundAsync ─────────────────────────────────────────────────

    [Test]
    public async Task LoadFundAsync_NoStep07Output_ThrowsAndReportsFailed()
    {
        // Arrange — step 7 stored no record, leaving its column empty.
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.ThesisValidator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.Recommender && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task LoadFundAsync_OutputLacksThisFund_Throws()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.ThesisValidator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [MakeFund("LU0000000099")]));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));
    }

    [Test]
    public async Task LoadFundAsync_Always_ReadsStep07ColumnOnly()
    {
        // Arrange — every step reads its predecessor's column, and step 8 reads nothing else.
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.LoadFundAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.ReadStepOutputAsync(
                StepId.ThesisValidator, Signal.RunId, It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.ReadStepOutputAsync(
                It.Is<StepId>(s => s != StepId.ThesisValidator), It.IsAny<PipelineRunId>(),
                It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    // ── RunAgentAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_AfterLoad_RunsAgentOnStep07Record()
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
                Week,
                Signal.RunId,
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step07Record)), Times.Once);
        });
    }

    [Test]
    public async Task RunAgentAsync_AgentWarns_StillSucceeds()
    {
        // Arrange — a null signal is a warning and a Skip, not a failure.
        var warned = MakeOutput(
            new DataQuality { Warnings = ["fund LU0000000001 has null signal — recommendation=Skip"] },
            MakeFund(Signal.Isin.Value));
        _agent
            .Setup(x => x.RunInMemory(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>()))
            .Returns(warned);
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
            .Setup(x => x.RunInMemory(
                It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<PipelineRunId>(),
                It.IsAny<IReadOnlyList<FundRecord>>()))
            .Throws(new InvalidOperationException("boom"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.Recommender && e.Kind == StepEventKind.Failed)), Times.Once);
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
    public async Task PersistAsync_AfterAgent_SavesAgentOutputToStep08Column()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync(Signal);

        // Assert
        _progress.Verify(x => x.SaveStepOutputAsync(
            Signal.Isin,
            StepId.Recommender,
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

    [Test]
    public async Task PersistAsync_StoreThrows_ThrowsAndReportsFailed()
    {
        // Arrange — the emit must not announce an output that was never stored.
        _progress
            .Setup(x => x.SaveStepOutputAsync(
                It.IsAny<Isin>(), It.IsAny<StepId>(), It.IsAny<PipelineRunId>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.PersistAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.Recommender && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── EmitDoneAsync ─────────────────────────────────────────────────

    [Test]
    public async Task EmitDoneAsync_AfterComplete_PublishesStep08DoneAndReportsNothing()
    {
        // Arrange — the complete already reported the fund done.
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);
        await sut.PersistAsync(Signal);
        await sut.CompleteAsync(Signal);
        _stepEvents.Invocations.Clear();

        // Act
        await sut.EmitDoneAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _signals.Verify(x => x.PublishAsync(
                new Step08DoneSignal(Signal.Isin, Signal.NavDate, Signal.RunId),
                It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    [Test]
    public async Task EmitDoneAsync_PublishThrows_ThrowsWithoutReportingFailed()
    {
        // Arrange — the fund is complete and stored; only step 9's early check is lost.
        _signals
            .Setup(x => x.PublishAsync(It.IsAny<Step08DoneSignal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);
        await sut.PersistAsync(Signal);
        await sut.CompleteAsync(Signal);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.EmitDoneAsync(Signal));

        // Assert
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.Recommender && e.Kind == StepEventKind.Failed)), Times.Never);
    }

    // ── CompleteAsync ─────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_AfterStart_CompletesStep08AndReportsSucceeded()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.CompleteAsync(Signal);

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.CompleteStep08Async(Signal.Isin, It.IsAny<CancellationToken>()), Times.Once);
            _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
                e.Step == StepId.Recommender && e.Kind == StepEventKind.Succeeded)), Times.Once);
        });
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
    public async Task CompleteAsync_StoreThrows_ReportsFailedAndRethrows()
    {
        // Arrange — rethrown so the caller fails the run instead of leaving it in flight.
        _progress
            .Setup(x => x.CompleteStep08Async(Signal.Isin, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(Signal));
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.Recommender && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── FailAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task FailAsync_AfterStart_FailsStep08WithErrorMessage()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("no step 7 record"));

        // Assert
        _progress.Verify(x => x.FailStep08Async(
            Signal.Isin, "no step 7 record", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FailAsync_StartRefused_LeavesRowAlone()
    {
        // Arrange — the row belongs to a newer run; failing it would free that run's lock.
        _progress
            .Setup(x => x.StartStep08Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("anything"));

        // Assert
        _progress.Verify(x => x.FailStep08Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StoreThrows_DoesNotThrow()
    {
        // Arrange
        _progress
            .Setup(x => x.FailStep08Async(Signal.Isin, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => sut.FailAsync(Signal, new InvalidOperationException("boom")));
    }

    // ── ReadOutputAsync ───────────────────────────────────────────────

    [Test]
    public async Task ReadOutputAsync_ReadsStep08Column()
    {
        // Arrange
        var stored = new StepOutput("[]", _agentOutput.Funds);
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.Recommender, Signal.RunId, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.ReadStepOutputAsync(StepId.Recommender, Signal.RunId, It.IsAny<CancellationToken>()))
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
    public async Task FailOrphanedRunsAsync_TwoOrphaned_FailsEachAtStep8AndReturnsCount()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep08Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000081"), Row("LU0000000082")]);
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(2));
            _progress.Verify(x => x.FailStep08Async(
                new Isin("LU0000000081"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(x => x.FailStep08Async(
                new Isin("LU0000000082"), It.Is<string>(m => m.StartsWith("Orphaned run")), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_NoneOrphaned_ReturnsZeroAndFailsNothing()
    {
        // Arrange
        _progress
            .Setup(x => x.ListOrphanedStep08Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IsinProgressEntity>());
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.Zero);
            _progress.Verify(x => x.FailStep08Async(
                It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task FailOrphanedRunsAsync_OneFailThrows_CarriesOnWithTheRest()
    {
        // Arrange — one bad row must not leave the others locked.
        _progress
            .Setup(x => x.ListOrphanedStep08Async(SessionStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("LU0000000081"), Row("LU0000000082")]);
        _progress
            .Setup(x => x.FailStep08Async(new Isin("LU0000000081"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(SessionStart);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(1));
            _progress.Verify(x => x.FailStep08Async(
                new Isin("LU0000000082"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    // ── ComputeOutputAsync ────────────────────────────────────────────

    [Test]
    public async Task ComputeOutputAsync_Step07Stored_RunsAgentOnStep07Records()
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
                It.Is<IReadOnlyList<FundRecord>>(f => f.Count == 1 && f[0] == _step07Record)), Times.Once);
        });
    }

    [Test]
    public void ComputeOutputAsync_NoStep07Output_ThrowsInvalidOperationException()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.ThesisValidator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComputeOutputAsync(Signal.RunId, Week));
    }

    [Test]
    public async Task ComputeOutputAsync_Always_NeitherStartsNorStoresNorReports()
    {
        // Arrange — a debug run must not take the fund's lock or replace what step 9 reads.
        var sut = CreateSut();

        // Act
        await sut.ComputeOutputAsync(Signal.RunId, Week);

        // Assert
        Assert.Multiple(() =>
        {
            _progress.Verify(x => x.StartStep08Async(
                It.IsAny<Isin>(), It.IsAny<PipelineRunId>(), It.IsAny<CancellationToken>()), Times.Never);
            _progress.Verify(x => x.SaveStepOutputAsync(
                It.IsAny<Isin>(), It.IsAny<StepId>(), It.IsAny<PipelineRunId>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<CancellationToken>()), Times.Never);
            _signals.Verify(x => x.PublishAsync(
                It.IsAny<Step08DoneSignal>(), It.IsAny<CancellationToken>()), Times.Never);
            _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
        });
    }

    private static IsinProgressEntity Row(string isin) => new() { Isin = isin };

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
