using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Analytics;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Fetch;
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
[TestOf(typeof(Step03MacroAnalystHandler))]
public sealed class Step03MacroAnalystHandlerTests
{
    // 2026-06-05 falls in 2026-W23.
    private static readonly Step02DoneSignal Signal = new(
        new Isin("LU0000000001"),
        new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
        new PipelineRunId("run-1"));

    private static readonly IsoWeek Week = IsoWeek.From("2026-W23");
    private const string WeeklySummaryRunId = "ws-run-23";

    private IFixture _fixture = null!;
    private Mock<IIsinProgressStore> _progress = null!;
    private Mock<IWeeklyAnalyticsReportsProvider> _reports = null!;
    private Mock<IMacroContextStore> _contexts = null!;
    private Mock<IFundMetadataProvider> _metadata = null!;
    private Mock<IStep03MacroAnalystPromptsProvider> _prompts = null!;
    private Mock<IMacroAnalystAgent> _agent = null!;
    private Mock<IPipelineSignals> _signals = null!;
    private Mock<IStepEventPublisher> _stepEvents = null!;

    private FundRecord _step02Record = null!;
    private WeeklyAnalyticsReports _weeklyReports = null!;
    private MacroContext _computed = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { CompanyFilter = "TestCo" });
        _fixture.Inject(new MacroContextSingleFlight());
        _progress = _fixture.Freeze<Mock<IIsinProgressStore>>();
        _reports = _fixture.Freeze<Mock<IWeeklyAnalyticsReportsProvider>>();
        _contexts = _fixture.Freeze<Mock<IMacroContextStore>>();
        _metadata = _fixture.Freeze<Mock<IFundMetadataProvider>>();
        _prompts = _fixture.Freeze<Mock<IStep03MacroAnalystPromptsProvider>>();
        _agent = _fixture.Freeze<Mock<IMacroAnalystAgent>>();
        _signals = _fixture.Freeze<Mock<IPipelineSignals>>();
        _stepEvents = _fixture.Freeze<Mock<IStepEventPublisher>>();

        _step02Record = MakeFund(Signal.Isin.Value, "Globalfond");
        _weeklyReports = MakeReports(Week.Value, WeeklySummaryRunId);
        _computed = MakeContext(Week.Value, WeeklySummaryRunId);

        // The miss path; tests override the one piece they are about.
        _progress
            .Setup(x => x.StartStep03Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IsinProgressEntity { Isin = Signal.Isin.Value });
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MetricsCalculator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepOutput("[]", [_step02Record]));
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_weeklyReports);
        _metadata
            .Setup(x => x.ListMetadataAsync(It.IsAny<Company>(), Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_step02Record.Metadata, MakeFund("LU0000000002", "Branschfond, Energi").Metadata]);
        _prompts
            .Setup(x => x.GetPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step03MacroAnalystPrompts("system", "user", "retry"));
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<WeeklySummaryRun>(), It.IsAny<SubstitutionChainRun>(), It.IsAny<OpportunityScanRun>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<IsoWeek>(), It.IsAny<Step03MacroAnalystPrompts>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_computed);
    }

    private Step03MacroAnalystHandler CreateSut() => _fixture.Create<Step03MacroAnalystHandler>();

    /// <summary>Runs every phase up to and including the agent.</summary>
    private static async Task<MacroContext> RunThroughAgentAsync(Step03MacroAnalystHandler sut)
    {
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);
        await sut.AssembleAgentInputAsync(Signal);
        return await sut.RunAgentAsync(Signal);
    }

    private void VerifyAgentNeverCalled() =>
        _agent.Verify(x => x.RunInMemoryAsync(
            It.IsAny<WeeklySummaryRun>(), It.IsAny<SubstitutionChainRun>(), It.IsAny<OpportunityScanRun>(),
            It.IsAny<DataLoaderOutput>(), It.IsAny<IsoWeek>(), It.IsAny<Step03MacroAnalystPrompts>(),
            It.IsAny<CancellationToken>()), Times.Never);

    // ── BeginProcessingAsync ──────────────────────────────────────────

    [Test]
    public async Task BeginProcessingAsync_StartGranted_ReturnsTrueAndReportsStarted()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.That(begun, Is.True);
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.MacroAnalyst && e.Kind == StepEventKind.Started)), Times.Once);
    }

    [Test]
    public async Task BeginProcessingAsync_StartRefused_ReturnsFalseAndReportsNothing()
    {
        // Arrange — a late signal.
        _progress
            .Setup(x => x.StartStep03Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.That(begun, Is.False);
        _stepEvents.Verify(x => x.Publish(It.IsAny<StepEvent>()), Times.Never);
    }

    [Test]
    public async Task BeginProcessingAsync_StartThrows_ReturnsFalseAndReportsFailed()
    {
        // Arrange
        _progress
            .Setup(x => x.StartStep03Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var begun = await sut.BeginProcessingAsync(Signal);

        // Assert
        Assert.That(begun, Is.False);
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.MacroAnalyst && e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── LoadFundAsync ─────────────────────────────────────────────────

    [Test]
    public async Task LoadFundAsync_NoStep02Output_ThrowsAndReportsFailed()
    {
        // Arrange
        _progress
            .Setup(x => x.ReadStepOutputAsync(StepId.MetricsCalculator, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepOutput?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task LoadFundAsync_NoReportsAndNothingStored_ThrowsAndReportsFailed()
    {
        // Arrange — the fourth row of the fallback order: nothing honest to use.
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WeeklyAnalyticsReports?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadFundAsync(Signal));
        Assert.That(ex!.Message, Does.Contain("2026-W23"));
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Once);
    }

    // ── Get or compute ────────────────────────────────────────────────

    [Test]
    public async Task RunAgentAsync_ContextStoredForTheReports_UsesItWithoutTheLlm()
    {
        // Arrange — the normal hit.
        var stored = MakeContext(Week.Value, WeeklySummaryRunId);
        _contexts
            .Setup(x => x.GetAsync(WeeklySummaryRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var sut = CreateSut();

        // Act
        var context = await RunThroughAgentAsync(sut);

        // Assert
        Assert.That(context, Is.SameAs(stored));
        VerifyAgentNeverCalled();
        _metadata.Verify(x => x.ListMetadataAsync(
            It.IsAny<Company>(), It.IsAny<IsoWeek>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RunAgentAsync_Miss_ComputesForTheWeekAndStoresIt()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var context = await RunThroughAgentAsync(sut);

        // Assert — the universe holds every fund's category, stamped with the fund's week.
        Assert.That(context, Is.SameAs(_computed));
        _agent.Verify(x => x.RunInMemoryAsync(
            _weeklyReports.WeeklySummary, _weeklyReports.SubstitutionChain, _weeklyReports.RotationTargets,
            It.Is<DataLoaderOutput>(u => u.IsoWeek == Week.Value && u.Funds.Count == 2),
            Week,
            It.Is<Step03MacroAnalystPrompts>(p => p.SystemPrompt == "system"),
            It.IsAny<CancellationToken>()), Times.Once);
        _contexts.Verify(x => x.SaveAsync(_computed, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_StoredBetweenLookupAndCompute_UsesItWithoutTheLlm()
    {
        // Arrange — another fund of the week stored the context after this one's lookup.
        var storedMeanwhile = MakeContext(Week.Value, WeeklySummaryRunId);
        _contexts
            .SetupSequence(x => x.GetAsync(WeeklySummaryRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MacroContext?)null)
            .ReturnsAsync(storedMeanwhile);
        var sut = CreateSut();

        // Act
        var context = await RunThroughAgentAsync(sut);

        // Assert
        Assert.That(context, Is.SameAs(storedMeanwhile));
        VerifyAgentNeverCalled();
    }

    [Test]
    public async Task RunAgentAsync_TwoFundsMissTogether_CallTheLlmOnce()
    {
        // Arrange — the agent is held open, so the second fund arrives mid-compute.
        var release = new TaskCompletionSource<MacroContext>();
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<WeeklySummaryRun>(), It.IsAny<SubstitutionChainRun>(), It.IsAny<OpportunityScanRun>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<IsoWeek>(), It.IsAny<Step03MacroAnalystPrompts>(),
                It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        var first = CreateSut();
        var second = CreateSut();

        // Act
        var firstRun = RunThroughAgentAsync(first);
        var secondRun = RunThroughAgentAsync(second);
        await Task.Delay(50);
        release.SetResult(_computed);
        var contexts = await Task.WhenAll(firstRun, secondRun);

        // Assert
        Assert.That(contexts, Is.All.SameAs(_computed));
        _agent.Verify(x => x.RunInMemoryAsync(
            It.IsAny<WeeklySummaryRun>(), It.IsAny<SubstitutionChainRun>(), It.IsAny<OpportunityScanRun>(),
            It.IsAny<DataLoaderOutput>(), It.IsAny<IsoWeek>(), It.IsAny<Step03MacroAnalystPrompts>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_NoReportsButEarlierContextStored_UsesItWithoutTheLlm()
    {
        // Arrange — the third row of the fallback order.
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WeeklyAnalyticsReports?)null);
        var lastWeek = MakeContext("2026-W22", "ws-run-22");
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastWeek);
        var sut = CreateSut();

        // Act
        var context = await RunThroughAgentAsync(sut);

        // Assert
        Assert.That(context, Is.SameAs(lastWeek));
        VerifyAgentNeverCalled();
    }

    [Test]
    public async Task AssembleAgentInputAsync_EmptyUniverse_ThrowsAndReportsFailed()
    {
        // Arrange — an empty universe would store an empty context for the whole week.
        _metadata
            .Setup(x => x.ListMetadataAsync(It.IsAny<Company>(), Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.AssembleAgentInputAsync(Signal));
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_AgentThrows_ThrowsReportsFailedAndStoresNothing()
    {
        // Arrange
        _agent
            .Setup(x => x.RunInMemoryAsync(
                It.IsAny<WeeklySummaryRun>(), It.IsAny<SubstitutionChainRun>(), It.IsAny<OpportunityScanRun>(),
                It.IsAny<DataLoaderOutput>(), It.IsAny<IsoWeek>(), It.IsAny<Step03MacroAnalystPrompts>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM returned invalid response after retry"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);
        await sut.LoadFundAsync(Signal);
        await sut.AssembleAgentInputAsync(Signal);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync(Signal));
        _contexts.Verify(x => x.SaveAsync(It.IsAny<MacroContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e => e.Kind == StepEventKind.Failed)), Times.Once);
    }

    [Test]
    public void RunAgentAsync_BeforeLoad_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAgentAsync(Signal));
    }

    // ── PersistAsync / EmitDoneAsync ──────────────────────────────────

    [Test]
    public async Task PersistAsync_AfterAgent_SavesStep02RecordUnchangedToStep03Column()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);

        // Act
        await sut.PersistAsync(Signal);

        // Assert
        _progress.Verify(x => x.SaveStepOutputAsync(
            Signal.Isin, StepId.MacroAnalyst, Signal.RunId,
            It.Is<DataLoaderOutput>(o => o.Funds.Count == 1 && ReferenceEquals(o.Funds[0], _step02Record)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void PersistAsync_BeforeAgent_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.PersistAsync(Signal));
    }

    [Test]
    public async Task EmitDoneAsync_AfterPersist_PublishesStep03DoneAndReportsSucceeded()
    {
        // Arrange
        var sut = CreateSut();
        await RunThroughAgentAsync(sut);
        await sut.PersistAsync(Signal);

        // Act
        await sut.EmitDoneAsync(Signal);

        // Assert
        _signals.Verify(x => x.PublishAsync(
            new Step03DoneSignal(Signal.Isin, Signal.NavDate, Signal.RunId), It.IsAny<CancellationToken>()), Times.Once);
        _stepEvents.Verify(x => x.Publish(It.Is<StepEvent>(e =>
            e.Step == StepId.MacroAnalyst && e.Kind == StepEventKind.Succeeded)), Times.Once);
    }

    // ── CompleteAsync / FailAsync ─────────────────────────────────────

    [Test]
    public async Task CompleteAsync_AfterStart_CompletesStep03()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.CompleteAsync(Signal);

        // Assert
        _progress.Verify(x => x.CompleteStep03Async(Signal.Isin, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void CompleteAsync_NotStarted_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(Signal));
    }

    [Test]
    public async Task FailAsync_AfterStart_FailsStep03WithErrorMessage()
    {
        // Arrange
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("no analytics reports"));

        // Assert
        _progress.Verify(x => x.FailStep03Async(Signal.Isin, "no analytics reports", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FailAsync_StartRefused_LeavesRowAlone()
    {
        // Arrange
        _progress
            .Setup(x => x.StartStep03Async(Signal.Isin, Signal.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IsinProgressEntity?)null);
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act
        await sut.FailAsync(Signal, new InvalidOperationException("boom"));

        // Assert
        _progress.Verify(x => x.FailStep03Async(
            It.IsAny<Isin>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FailAsync_StoreThrows_DoesNotThrow()
    {
        // Arrange
        _progress
            .Setup(x => x.FailStep03Async(Signal.Isin, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();
        await sut.BeginProcessingAsync(Signal);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => sut.FailAsync(Signal, new InvalidOperationException("boom")));
    }

    // ── FailOrphanedRunsAsync ─────────────────────────────────────────

    [Test]
    public async Task FailOrphanedRunsAsync_TwoOrphaned_FailsEachAtStep3AndReturnsCount()
    {
        // Arrange
        var startedBefore = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        _progress
            .Setup(x => x.ListOrphanedStep03Async(startedBefore, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IsinProgressEntity { Isin = "LU0000000011" },
                new IsinProgressEntity { Isin = "LU0000000012" },
            ]);
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(startedBefore);

        // Assert
        Assert.That(failed, Is.EqualTo(2));
        _progress.Verify(x => x.FailStep03Async(
            It.IsAny<Isin>(), It.Is<string>(m => m.Contains("Orphaned")), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task FailOrphanedRunsAsync_OneFailThrows_CarriesOnWithTheRest()
    {
        // Arrange
        var startedBefore = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        _progress
            .Setup(x => x.ListOrphanedStep03Async(startedBefore, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IsinProgressEntity { Isin = "LU0000000011" },
                new IsinProgressEntity { Isin = "LU0000000012" },
            ]);
        _progress
            .Setup(x => x.FailStep03Async(new Isin("LU0000000011"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unreachable"));
        var sut = CreateSut();

        // Act
        var failed = await sut.FailOrphanedRunsAsync(startedBefore);

        // Assert
        Assert.That(failed, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAgentAsync_OnlyEarlierReports_ComputesForTheReportsWeek()
    {
        // Arrange — the export is stale: the latest reports are for 2026-W18.
        var stale = MakeReports("2026-W18", "ws-run-18");
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);
        var sut = CreateSut();

        // Act
        await RunThroughAgentAsync(sut);

        // Assert — computed and stamped for the reports' week, never the fund's, so the
        // agent's week check holds and the context does not claim a week it is not from.
        var reportsWeek = IsoWeek.From("2026-W18");
        _agent.Verify(x => x.RunInMemoryAsync(
            stale.WeeklySummary, stale.SubstitutionChain, stale.RotationTargets,
            It.Is<DataLoaderOutput>(u => u.IsoWeek == "2026-W18"),
            reportsWeek,
            It.IsAny<Step03MacroAnalystPrompts>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunAgentAsync_EarlierReportsButNewerContextStored_UsesTheStoredOne()
    {
        // Arrange — W22 was computed from an export since replaced by an older one.
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeReports("2026-W18", "ws-run-18"));
        var newer = MakeContext("2026-W22", "ws-run-22");
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newer);
        var sut = CreateSut();

        // Act
        var context = await RunThroughAgentAsync(sut);

        // Assert
        Assert.That(context, Is.SameAs(newer));
        VerifyAgentNeverCalled();
    }

    // ── ReadContextAsync ──────────────────────────────────────────────

    [Test]
    public async Task ReadContextAsync_ReturnsTheNewestContextUpToTheWeek()
    {
        // Arrange
        var lastWeek = MakeContext("2026-W22", "ws-run-22");
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastWeek);

        // Act
        var context = await CreateSut().ReadContextAsync(Week);

        // Assert
        Assert.That(context, Is.SameAs(lastWeek));
    }

    [Test]
    public async Task ReadContextAsync_StoreThrows_ReturnsNull()
    {
        // Arrange — a read feeds a view; a failure costs the display, not a crash.
        _contexts
            .Setup(x => x.GetNewestAsync(Week, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no such table: MacroContexts"));

        // Act
        var context = await CreateSut().ReadContextAsync(Week);

        // Assert
        Assert.That(context, Is.Null);
    }

    // ── ComputeContextAsync ───────────────────────────────────────────

    [Test]
    public async Task ComputeContextAsync_ReportsFound_ReturnsTheAgentsContextWithoutTouchingTheStore()
    {
        // Act
        var context = await CreateSut().ComputeContextAsync(Week);

        // Assert — a debug run: no cached context reused, none replaced.
        Assert.That(context, Is.SameAs(_computed));
        _contexts.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ComputeContextAsync_OnlyEarlierReports_ComputesForTheReportsWeek()
    {
        // Arrange — the agent's week check needs the universe stamped with the reports' week.
        var lastWeek = IsoWeek.From("2026-W22");
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeReports(lastWeek.Value, "ws-run-22"));

        // Act
        await CreateSut().ComputeContextAsync(Week);

        // Assert
        _agent.Verify(x => x.RunInMemoryAsync(
            It.IsAny<WeeklySummaryRun>(), It.IsAny<SubstitutionChainRun>(), It.IsAny<OpportunityScanRun>(),
            It.Is<DataLoaderOutput>(u => u.IsoWeek == lastWeek.Value && u.Funds.Count == 2),
            lastWeek, It.IsAny<Step03MacroAnalystPrompts>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ComputeContextAsync_NoReports_Throws()
    {
        // Arrange
        _reports
            .Setup(x => x.GetLatestAsync(Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WeeklyAnalyticsReports?)null);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().ComputeContextAsync(Week));
        VerifyAgentNeverCalled();
    }

    [Test]
    public void ComputeContextAsync_EmptyUniverse_Throws()
    {
        // Arrange
        _metadata
            .Setup(x => x.ListMetadataAsync(It.IsAny<Company>(), Week, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().ComputeContextAsync(Week));
        VerifyAgentNeverCalled();
    }

    // ── Builders ──────────────────────────────────────────────────────

    private static FundRecord MakeFund(string isin, string category) => new()
    {
        Isin = new Isin(isin),
        Metadata = new FundMetadata
        {
            Isin = new Isin(isin),
            Name = $"Fund {isin}",
            CompanyName = "TestCo",
            CurrencyCode = "SEK",
            Category = category,
            FundType = "EQUITY_FUND",
            ManagedType = "ACTIVE",
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = [],
    };

    private static WeeklyAnalyticsReports MakeReports(string week, string weeklySummaryRunId) => new(
        new WeeklySummaryRun
        {
            ReportType = "weekly-summary",
            RunId = weeklySummaryRunId,
            Status = RunStatus.Success,
            PeriodIsoWeek = week,
            NetMood = MarketSentiment.Mixed,
            MoodSummary = "Mixed week.",
            Themes = [],
        },
        new SubstitutionChainRun
        {
            ReportType = "substitution-chain",
            RunId = $"{weeklySummaryRunId}-chain",
            Status = RunStatus.Success,
            PeriodIsoWeek = week,
            WeeklySummaryRunId = weeklySummaryRunId,
            Chains = [],
        },
        new OpportunityScanRun
        {
            ReportType = "rotation-targets",
            RunId = $"{weeklySummaryRunId}-targets",
            Status = RunStatus.Success,
            PeriodIsoWeek = week,
            SubstitutionChainRunId = $"{weeklySummaryRunId}-chain",
            Targets = [],
        });

    private static MacroContext MakeContext(string week, string weeklySummaryRunId) => new()
    {
        GeneratedAt = "2026-06-05T10:00:00.0000000+00:00",
        IsoWeek = week,
        ConfigVersion = "1.0.0",
        SourceRunIds = new SourceRunIds
        {
            WeeklySummaryRunId = weeklySummaryRunId,
            SubstitutionChainRunId = $"{weeklySummaryRunId}-chain",
            RotationTargetsRunId = $"{weeklySummaryRunId}-targets",
        },
        MacroRegime = MacroRegime.Mixed,
        RegimeConfidence = 0.5m,
        NetMoodInput = MarketSentiment.Mixed,
        Catalysts = [],
        RotationThemes = [],
    };
}
