using System.Reactive.Linq;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Portfolio;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(PipelineRunner))]
public sealed class PipelineRunnerTests
{
    private IFixture _fixture = null!;
    private Mock<IMetricsCalculatorAgent> _metrics = null!;
    private Mock<IMacroAnalystAgent> _macroAnalyst = null!;
    private Mock<ISignalScorerAgent> _signal = null!;
    private Mock<IMacroAlignerAgent> _macroAligner = null!;
    private Mock<ICatalystTaggerAgent> _catalyst = null!;
    private Mock<IThesisValidatorAgent> _thesis = null!;
    private Mock<IRecommenderAgent> _recommender = null!;
    private Mock<IUniverseEnricherAgent> _enricher = null!;
    private Mock<IStreamingPipelineGateway> _gateway = null!;
    private PipelineRunner _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // The five async agents need their RunAsync set up to return real
        // completed Tasks — AutoMoq's default leaves Task<T> returns as null,
        // which would NRE when the runner awaits them.
        _macroAnalyst = _fixture.Freeze<Mock<IMacroAnalystAgent>>();
        _macroAnalyst
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(MacroContext)!);

        _macroAligner = _fixture.Freeze<Mock<IMacroAlignerAgent>>();
        _macroAligner
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);
        _macroAligner
            .Setup(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundRecord f, IReadOnlyList<RotationTheme> _, CancellationToken _) =>
                new FundProcessingResult(f, Array.Empty<string>()));

        _catalyst = _fixture.Freeze<Mock<ICatalystTaggerAgent>>();
        _catalyst
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);
        _catalyst
            .Setup(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundRecord f, IReadOnlyList<Catalyst> _, CancellationToken _) =>
                new FundProcessingResult(f, Array.Empty<string>()));

        _thesis = _fixture.Freeze<Mock<IThesisValidatorAgent>>();
        _thesis
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);
        _thesis
            .Setup(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundRecord f, CancellationToken _) =>
                new FundProcessingResult(f, Array.Empty<string>()));

        _enricher = _fixture.Freeze<Mock<IUniverseEnricherAgent>>();
        _enricher
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);

        // The three sync per-ISIN agents — AutoMoq returns null for ProcessFund
        // (FundRecord is a class), which would break the per-fund chain. Stub
        // them to pass the input fund through unchanged.
        _metrics = _fixture.Freeze<Mock<IMetricsCalculatorAgent>>();
        _metrics
            .Setup(x => x.ProcessFund(It.IsAny<FundRecord>(), It.IsAny<MetricsCalculatorConfig>()))
            .Returns((FundRecord f, MetricsCalculatorConfig _) => f);

        _signal = _fixture.Freeze<Mock<ISignalScorerAgent>>();
        _signal
            .Setup(x => x.ProcessFund(It.IsAny<FundRecord>(), It.IsAny<SignalScorerConfig>()))
            .Returns((FundRecord f, SignalScorerConfig _) => f);

        _recommender = _fixture.Freeze<Mock<IRecommenderAgent>>();
        _recommender
            .Setup(x => x.ProcessFund(It.IsAny<FundRecord>()))
            .Returns((FundRecord f) => new FundProcessingResult(f, Array.Empty<string>()));

        // The streaming gateway is mocked here; tests that exercise
        // RunAllStreamingAsync stub LoadStep1Output / LoadStep3Output to
        // return real fixtures and verify SaveStepOutput calls.
        _gateway = _fixture.Freeze<Mock<IStreamingPipelineGateway>>();
        _gateway
            .Setup(x => x.LoadMetricsConfig())
            .Returns(MetricsCalculatorConfig.Default);
        _gateway
            .Setup(x => x.LoadSignalConfig())
            .Returns(SignalScorerConfig.Default);

        _sut = _fixture.Create<PipelineRunner>();
    }

    [TearDown]
    public void TearDown()
    {
        _sut.Dispose();
    }

    [Test]
    public async Task RunAllAsync_AllStepsSucceed_ReturnsTrue()
    {
        var result = await _sut.RunAllAsync("OPM", "2026-W21", "20260524-1200");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RunAllAsync_AllStepsSucceed_EmitsSucceededForEveryStep()
    {
        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        await _sut.RunAllAsync("OPM", "2026-W21", "run-1");

        var succeededSteps = observed
            .Where(e => e.Kind == StepEventKind.Succeeded)
            .Select(e => e.Step.Value)
            .ToList();
        Assert.That(succeededSteps, Is.EqualTo(Enumerable.Range(1, 10).ToList()));
    }

    [Test]
    public async Task RunAllAsync_StepThrows_EmitsFailedAndReturnsFalse()
    {
        _macroAnalyst
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream JSON unreadable"));

        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        var result = await _sut.RunAllAsync("OPM", "2026-W21", "run-2");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            var failed = observed.SingleOrDefault(e => e.Kind == StepEventKind.Failed);
            Assert.That(failed, Is.Not.Null);
            Assert.That(failed!.Step, Is.EqualTo(StepId.MacroAnalyst));
            Assert.That(failed.Message, Does.Contain("upstream JSON unreadable"));
        });
    }

    [Test]
    public async Task RunAllAsync_Step3Throws_DoesNotInvokeStep4OrLater()
    {
        _macroAnalyst
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));
        var signal = _fixture.Freeze<Mock<ISignalScorerAgent>>();

        await _sut.RunAllAsync("OPM", "2026-W21", "run-3");

        signal.Verify(
            x => x.Run(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "signal scorer should not run after Step 3 failed");
        _macroAligner.Verify(
            x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "macro aligner should not run after Step 3 failed");
    }

    [Test]
    public void RunAllAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _sut.RunAllAsync("OPM", "2026-W21", "run-4", cts.Token));
    }

    [Test]
    public async Task RunStepAsync_AgentSucceeds_EmitsStartedThenSucceeded()
    {
        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        var result = await _sut.RunStepAsync(StepId.MacroAnalyst, "OPM", "2026-W21", "run-5");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(observed, Has.Count.EqualTo(2));
            Assert.That(observed[0].Kind, Is.EqualTo(StepEventKind.Started));
            Assert.That(observed[0].Step, Is.EqualTo(StepId.MacroAnalyst));
            Assert.That(observed[1].Kind, Is.EqualTo(StepEventKind.Succeeded));
            Assert.That(observed[1].Step, Is.EqualTo(StepId.MacroAnalyst));
            Assert.That(observed[1].Duration, Is.Not.Null);
        });
    }

    [Test]
    public async Task RunStepAsync_AgentThrows_EmitsFailedWithExceptionMessage()
    {
        _enricher
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("step 8 output missing"));

        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        var result = await _sut.RunStepAsync(StepId.UniverseEnricher, "OPM", "2026-W21", "run-6");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            var failed = observed.Last();
            Assert.That(failed.Kind, Is.EqualTo(StepEventKind.Failed));
            Assert.That(failed.Step, Is.EqualTo(StepId.UniverseEnricher));
            Assert.That(failed.Message, Is.EqualTo("step 8 output missing"));
            Assert.That(failed.Duration, Is.Not.Null);
        });
    }

    [Test]
    public void StepId_FromInvalidValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StepId.From(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepId.From(11));
    }

    [Test]
    public void StepId_AllHasTenSteps_InOrder()
    {
        Assert.That(StepId.All, Has.Count.EqualTo(10));
        Assert.That(StepId.All.Select(s => s.Value), Is.EqualTo(Enumerable.Range(1, 10)));
    }

    [Test]
    public void StepId_StaticFields_AgentNamesMatchSteps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StepId.DataLoader.AgentName,           Is.EqualTo("DataLoader"));
            Assert.That(StepId.MetricsCalculator.AgentName,    Is.EqualTo("MetricsCalculator"));
            Assert.That(StepId.MacroAnalyst.AgentName,         Is.EqualTo("MacroAnalyst"));
            Assert.That(StepId.SignalScorer.AgentName,         Is.EqualTo("SignalScorer"));
            Assert.That(StepId.MacroAligner.AgentName,         Is.EqualTo("MacroAligner"));
            Assert.That(StepId.CatalystTagger.AgentName,       Is.EqualTo("CatalystTagger"));
            Assert.That(StepId.ThesisValidator.AgentName,      Is.EqualTo("ThesisValidator"));
            Assert.That(StepId.Recommender.AgentName,          Is.EqualTo("Recommender"));
            Assert.That(StepId.UniverseEnricher.AgentName,     Is.EqualTo("UniverseEnricher"));
            Assert.That(StepId.PortfolioConstructor.AgentName, Is.EqualTo("PortfolioConstructor"));
        });
    }

    [Test]
    public async Task RunAllAsync_AllStepsSucceed_AllStepEventsHaveNullIsin()
    {
        // Current runner is whole-universe per step — no per-fund ticks yet.
        // When per-ISIN streaming lands, Steps 2/4/5/6/7/8 will emit events
        // with Isin populated.
        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        await _sut.RunAllAsync("OPM", "2026-W21", "isin-check");

        Assert.That(observed, Is.Not.Empty);
        Assert.That(observed.All(e => e.Isin is null), Is.True,
            "every event from the sequential runner should carry Isin = null");
    }

    [Test]
    public void StepEvent_DefaultIsinIsNull()
    {
        var evt = new StepEvent(StepId.MetricsCalculator, StepEventKind.Started);

        Assert.That(evt.Isin, Is.Null);
    }

    [Test]
    public void StepEvent_CanCarryIsin()
    {
        var isin = new FikaFinans.Domain.Identifiers.Isin("LU0001000001");
        var evt = new StepEvent(StepId.MetricsCalculator, StepEventKind.Succeeded, Isin: isin);

        Assert.That(evt.Isin, Is.EqualTo(isin));
    }

    // ───────────────────────── RunPerIsinBlockAsync ─────────────────────────

    [Test]
    public async Task RunPerIsinBlockAsync_ProcessesEveryFundThroughAllSixSteps()
    {
        var step1 = MakeStep1Output(MakeFund("LU0000000001"), MakeFund("LU0000000002"));
        var macro = MakeMacroContext();

        await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 2);

        Assert.Multiple(() =>
        {
            _metrics.Verify(x => x.ProcessFund(
                It.IsAny<FundRecord>(), It.IsAny<MetricsCalculatorConfig>()), Times.Exactly(2));
            _signal.Verify(x => x.ProcessFund(
                It.IsAny<FundRecord>(), It.IsAny<SignalScorerConfig>()), Times.Exactly(2));
            _macroAligner.Verify(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(), It.IsAny<IReadOnlyList<RotationTheme>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _catalyst.Verify(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(), It.IsAny<IReadOnlyList<Catalyst>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _thesis.Verify(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _recommender.Verify(x => x.ProcessFund(It.IsAny<FundRecord>()), Times.Exactly(2));
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_EmitsStartedAndSucceededWithIsinForEveryStep()
    {
        var isin = "LU0000000001";
        var step1 = MakeStep1Output(MakeFund(isin));
        var macro = MakeMacroContext();
        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 1);

        var perIsinSteps = new[]
        {
            StepId.MetricsCalculator, StepId.SignalScorer, StepId.MacroAligner,
            StepId.CatalystTagger, StepId.ThesisValidator, StepId.Recommender,
        };

        Assert.Multiple(() =>
        {
            foreach (var step in perIsinSteps)
            {
                Assert.That(observed.Any(e =>
                    e.Step == step && e.Kind == StepEventKind.Started && e.Isin?.Value == isin),
                    Is.True, $"missing Started+Isin event for {step}");
                Assert.That(observed.Any(e =>
                    e.Step == step && e.Kind == StepEventKind.Succeeded && e.Isin?.Value == isin),
                    Is.True, $"missing Succeeded+Isin event for {step}");
            }
            Assert.That(observed.All(e => e.Isin is not null),
                "every event from the per-ISIN block should carry Isin");
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_ReturnsSixBoundaryOutputsPreservingFundCount()
    {
        var step1 = MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0002"), MakeFund("LU0003"));
        var macro = MakeMacroContext();

        var result = await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Step2Output.Funds, Has.Count.EqualTo(3));
            Assert.That(result.Step4Output.Funds, Has.Count.EqualTo(3));
            Assert.That(result.Step5Output.Funds, Has.Count.EqualTo(3));
            Assert.That(result.Step6Output.Funds, Has.Count.EqualTo(3));
            Assert.That(result.Step7Output.Funds, Has.Count.EqualTo(3));
            Assert.That(result.Step8Output.Funds, Has.Count.EqualTo(3));
            Assert.That(result.Step8Output.IsoWeek, Is.EqualTo(step1.IsoWeek));
            Assert.That(result.Step8Output.Family, Is.EqualTo(step1.Family));
            Assert.That(result.Step8Output.RunId, Is.EqualTo(step1.RunId));
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_PreservesInputFundOrderInEveryBoundaryOutput()
    {
        var step1 = MakeStep1Output(MakeFund("LU0003"), MakeFund("LU0001"), MakeFund("LU0002"));
        var macro = MakeMacroContext();

        var result = await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 3);

        var expectedOrder = new[] { "LU0003", "LU0001", "LU0002" };
        Assert.Multiple(() =>
        {
            Assert.That(result.Step2Output.Funds.Select(f => f.Isin.Value), Is.EqualTo(expectedOrder));
            Assert.That(result.Step8Output.Funds.Select(f => f.Isin.Value), Is.EqualTo(expectedOrder));
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_FoldsPerFundWarningsIntoEveryBoundaryDataQuality()
    {
        _macroAligner
            .Setup(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundRecord f, IReadOnlyList<RotationTheme> _, CancellationToken _) =>
                new FundProcessingResult(f, new[] { $"warn-{f.Isin.Value}" }));

        var step1 = MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0002"));
        var macro = MakeMacroContext();

        var result = await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Step5Output.DataQuality.Warnings, Has.Member("warn-LU0001"));
            Assert.That(result.Step5Output.DataQuality.Warnings, Has.Member("warn-LU0002"));
            Assert.That(result.Step8Output.DataQuality.Warnings, Has.Member("warn-LU0001"));
            Assert.That(result.Step8Output.DataQuality.Warnings, Has.Member("warn-LU0002"));
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_StepThrows_EmitsFailedWithIsinAndDropsFundFromBoundaryOutputs()
    {
        // Per Open Question #6 resolution: a per-fund failure must NOT
        // propagate out of the per-ISIN block — the failing fund's Failed
        // StepEvent fires, the fund is dropped from every boundary snapshot,
        // and the merge continues. (Sibling-survives behaviour is verified by
        // RunPerIsinBlockAsync_OneFundThrows_DropsFundAndContinuesOthers.)
        var failingIsin = "LU0099";
        _thesis
            .Setup(x => x.ProcessFundAsync(
                It.Is<FundRecord>(f => f.Isin.Value == failingIsin),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM exploded"));

        var step1 = MakeStep1Output(MakeFund(failingIsin));
        var macro = MakeMacroContext();
        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        var result = await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 1);

        Assert.Multiple(() =>
        {
            var failed = observed.SingleOrDefault(e => e.Kind == StepEventKind.Failed);
            Assert.That(failed, Is.Not.Null);
            Assert.That(failed!.Step, Is.EqualTo(StepId.ThesisValidator));
            Assert.That(failed.Isin?.Value, Is.EqualTo(failingIsin));
            Assert.That(failed.Message, Does.Contain("LLM exploded"));

            Assert.That(result.FailedFunds, Has.Count.EqualTo(1));
            Assert.That(result.FailedFunds[failingIsin], Does.Contain("LLM exploded"));
            Assert.That(result.Step2Output.Funds, Is.Empty, "failed fund is dropped from boundary outputs");
            Assert.That(result.Step8Output.Funds, Is.Empty);
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_OneFundThrows_DropsFundAndContinuesOthers()
    {
        // Closes pipeline-step-flow-plan.md Open Question #6 (Error routing):
        // one bad fund must drop out of the stream while siblings keep
        // streaming through the full per-ISIN chain.
        var failingIsin = "LU0099";
        _thesis
            .Setup(x => x.ProcessFundAsync(
                It.Is<FundRecord>(f => f.Isin.Value == failingIsin),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM exploded for LU0099"));

        var step1 = MakeStep1Output(
            MakeFund("LU0001"), MakeFund(failingIsin), MakeFund("LU0002"));
        var macro = MakeMacroContext();
        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        var result = await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 2);

        Assert.Multiple(() =>
        {
            // Failed fund dropped from every boundary
            Assert.That(result.FailedFunds.Keys, Is.EquivalentTo(new[] { failingIsin }));
            Assert.That(result.Step2Output.Funds.Select(f => f.Isin.Value),
                Is.EquivalentTo(new[] { "LU0001", "LU0002" }));
            Assert.That(result.Step8Output.Funds.Select(f => f.Isin.Value),
                Is.EquivalentTo(new[] { "LU0001", "LU0002" }));

            // Sibling funds emit Succeeded for the full chain
            foreach (var survivor in new[] { "LU0001", "LU0002" })
            {
                Assert.That(observed.Any(e =>
                        e.Step == StepId.Recommender
                        && e.Kind == StepEventKind.Succeeded
                        && e.Isin?.Value == survivor),
                    Is.True, $"{survivor} should complete the full per-ISIN chain");
            }

            // Failing fund emits exactly one Failed (at Step 7)
            var failed = observed.SingleOrDefault(e =>
                e.Kind == StepEventKind.Failed && e.Isin?.Value == failingIsin);
            Assert.That(failed, Is.Not.Null);
            Assert.That(failed!.Step, Is.EqualTo(StepId.ThesisValidator));
        });
    }

    [Test]
    public void RunPerIsinBlockAsync_NullArgs_Throws()
    {
        var step1 = MakeStep1Output(MakeFund("LU0001"));
        var macro = MakeMacroContext();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _sut.RunPerIsinBlockAsync(
                null!, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default, 1));
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _sut.RunPerIsinBlockAsync(
                step1, null!, MetricsCalculatorConfig.Default, SignalScorerConfig.Default, 1));
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _sut.RunPerIsinBlockAsync(
                step1, macro, null!, SignalScorerConfig.Default, 1));
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _sut.RunPerIsinBlockAsync(
                step1, macro, MetricsCalculatorConfig.Default, null!, 1));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await _sut.RunPerIsinBlockAsync(
                step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default, 0));
        });
    }

    // ───────────────────────── RunAllStreamingAsync ─────────────────────────

    [Test]
    public async Task RunAllStreamingAsync_AllStepsSucceed_ReturnsTrue()
    {
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeStep1Output(MakeFund("LU0001")));
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());

        var result = await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-1");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RunAllStreamingAsync_HappyPath_WritesAllSixPerIsinBoundaryFiles()
    {
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0002")));
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());

        await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-2", maxConcurrent: 2);

        Assert.Multiple(() =>
        {
            _gateway.Verify(x => x.SaveStepOutput(
                StepId.MetricsCalculator, "2026-W21", "stream-2", It.IsAny<DataLoaderOutput>()), Times.Once);
            _gateway.Verify(x => x.SaveStepOutput(
                StepId.SignalScorer, "2026-W21", "stream-2", It.IsAny<DataLoaderOutput>()), Times.Once);
            _gateway.Verify(x => x.SaveStepOutput(
                StepId.MacroAligner, "2026-W21", "stream-2", It.IsAny<DataLoaderOutput>()), Times.Once);
            _gateway.Verify(x => x.SaveStepOutput(
                StepId.CatalystTagger, "2026-W21", "stream-2", It.IsAny<DataLoaderOutput>()), Times.Once);
            _gateway.Verify(x => x.SaveStepOutput(
                StepId.ThesisValidator, "2026-W21", "stream-2", It.IsAny<DataLoaderOutput>()), Times.Once);
            _gateway.Verify(x => x.SaveStepOutput(
                StepId.Recommender, "2026-W21", "stream-2", It.IsAny<DataLoaderOutput>()), Times.Once);
        });
    }

    [Test]
    public async Task RunAllStreamingAsync_HappyPath_EmitsUniverseSucceededForAllTenSteps()
    {
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeStep1Output(MakeFund("LU0001")));
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());

        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-3");

        var universeSucceededSteps = observed
            .Where(e => e.Kind == StepEventKind.Succeeded && e.Isin is null)
            .Select(e => e.Step.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        Assert.That(universeSucceededSteps, Is.EqualTo(Enumerable.Range(1, 10).ToList()));
    }

    [Test]
    public async Task RunAllStreamingAsync_Step1Fails_DoesNotInvokeGatewayOrLaterSteps()
    {
        _fixture.Freeze<Mock<IDataLoaderAgent>>()
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("boom"));
        // Re-create SUT so the freshly-frozen DataLoader mock takes effect.
        _sut.Dispose();
        _sut = _fixture.Create<PipelineRunner>();

        var result = await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-4");

        Assert.That(result, Is.False);
        _gateway.Verify(
            x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "gateway should not be touched if Step 1 fails");
    }

    [Test]
    public async Task RunAllStreamingAsync_OneFundFails_RunSucceedsAndOtherFundsComplete()
    {
        // Per Open Q #6: per-fund failures no longer cascade to universe-level
        // Failed events. The run succeeds and surviving funds complete the
        // full per-ISIN chain. The failing fund still emits its per-fund
        // Failed event.
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0099"), MakeFund("LU0002")));
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());
        _thesis
            .Setup(x => x.ProcessFundAsync(
                It.Is<FundRecord>(f => f.Isin.Value == "LU0099"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM exploded for LU0099"));

        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        var result = await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-isolate-1");

        var perIsinSteps = new[]
        {
            StepId.MetricsCalculator, StepId.SignalScorer, StepId.MacroAligner,
            StepId.CatalystTagger, StepId.ThesisValidator, StepId.Recommender,
        };
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "isolated per-fund failures must not fail the run");
            foreach (var step in perIsinSteps)
            {
                Assert.That(observed.Any(e =>
                        e.Step == step && e.Kind == StepEventKind.Succeeded && e.Isin is null),
                    Is.True, $"universe-Succeeded must still fire for {step}");
                Assert.That(observed.Any(e =>
                        e.Step == step && e.Kind == StepEventKind.Failed && e.Isin is null),
                    Is.False, $"per-fund failure must not bubble to universe-level Failed for {step}");
            }
            Assert.That(observed.Any(e =>
                    e.Kind == StepEventKind.Failed && e.Isin?.Value == "LU0099"),
                Is.True, "failing fund still emits a per-fund Failed event");
        });
    }

    [Test]
    public void RunAllStreamingAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-6", ct: cts.Token));
    }

    [Test]
    public void RunAllStreamingAsync_CancelledMidStream_HaltsMergeAndDoesNotStartLaterFunds()
    {
        // Closes pipeline-step-flow-plan.md Open Question #7 (Cancellation):
        // confirms the Rx operator chain honours a token cancelled mid-flight
        // (not just pre-flight) by halting the per-ISIN Merge so funds queued
        // after the cancellation point never start.
        using var cts = new CancellationTokenSource();
        var step1 = MakeStep1Output(
            MakeFund("LU0001"), MakeFund("LU0002"),
            MakeFund("LU0003"), MakeFund("LU0004"));
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(step1);
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());

        // With maxConcurrent: 1 funds process sequentially in input order.
        // Cancel during the 2nd fund's Step 7 so LU0001 completes the chain,
        // LU0002's Step 7 throws OCE, and LU0003 + LU0004 must not start.
        var thesisInvocations = 0;
        _thesis
            .Setup(x => x.ProcessFundAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns((FundRecord f, CancellationToken _) =>
            {
                var n = Interlocked.Increment(ref thesisInvocations);
                if (n == 2)
                {
                    cts.Cancel();
                    return Task.FromException<FundProcessingResult>(
                        new OperationCanceledException(cts.Token));
                }
                return Task.FromResult(new FundProcessingResult(f, Array.Empty<string>()));
            });

        var observed = new List<StepEvent>();
        using var sub = _sut.Events.Subscribe(observed.Add);

        // ToTask(ct) surfaces a TaskCanceledException (subclass of OCE) when
        // the outer token cancels mid-stream — accept either exact type.
        Assert.That(async () => await _sut.RunAllStreamingAsync(
                "OPM", "2026-W21", "stream-cancel-mid",
                maxConcurrent: 1, ct: cts.Token),
            Throws.InstanceOf<OperationCanceledException>());

        var startedIsins = observed
            .Where(e => e.Kind == StepEventKind.Started && e.Isin is not null)
            .Select(e => e.Isin!.Value)
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(observed.Any(e =>
                    e.Step == StepId.Recommender
                    && e.Kind == StepEventKind.Succeeded
                    && e.Isin?.Value == "LU0001"),
                Is.True,
                "LU0001 should have completed the chain before cancellation fired");
            Assert.That(startedIsins, Does.Not.Contain("LU0003"),
                "LU0003 must not start once cancellation fires mid-stream");
            Assert.That(startedIsins, Does.Not.Contain("LU0004"),
                "LU0004 must not start once cancellation fires mid-stream");
        });
    }

    [Test]
    public async Task RunAllStreamingAsync_HappyPath_DrivesIsinProgressClaimBlockStep9Release()
    {
        var step1 = MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0002"));
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(step1);
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());

        await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-progress-1");

        Assert.Multiple(() =>
        {
            _gateway.Verify(x => x.ClaimIsinProgressAsync(step1, "stream-progress-1", It.IsAny<CancellationToken>()),
                Times.Once);
            _gateway.Verify(x => x.WriteIsinProgressBlockAsync(
                It.IsAny<PerIsinBlockResult>(), "stream-progress-1", It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.WriteIsinProgressStep9Async(
                "2026-W21", "stream-progress-1", It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.ReleaseIsinProgressAsync(
                step1, "stream-progress-1", It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task RunAllStreamingAsync_Step1Fails_DoesNotInvokeIsinProgressMethods()
    {
        _fixture.Freeze<Mock<IDataLoaderAgent>>()
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("boom"));
        _sut.Dispose();
        _sut = _fixture.Create<PipelineRunner>();

        await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-progress-2");

        Assert.Multiple(() =>
        {
            _gateway.Verify(x => x.ClaimIsinProgressAsync(
                It.IsAny<DataLoaderOutput>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _gateway.Verify(x => x.WriteIsinProgressBlockAsync(
                It.IsAny<PerIsinBlockResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _gateway.Verify(x => x.WriteIsinProgressStep9Async(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _gateway.Verify(x => x.ReleaseIsinProgressAsync(
                It.IsAny<DataLoaderOutput>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task RunAllStreamingAsync_OneFundFails_AllGatewayMethodsCalledIncludingMarkFundFailed()
    {
        // Per Open Q #6: per-fund failures are isolated, so the block
        // "succeeds" with the surviving funds; all four IsinProgress lifecycle
        // methods still fire, plus MarkFundFailedAsync once per failed fund.
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0099")));
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());
        _thesis
            .Setup(x => x.ProcessFundAsync(
                It.Is<FundRecord>(f => f.Isin.Value == "LU0099"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM exploded"));

        await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-isolate-2");

        Assert.Multiple(() =>
        {
            _gateway.Verify(x => x.ClaimIsinProgressAsync(
                It.IsAny<DataLoaderOutput>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.WriteIsinProgressBlockAsync(
                It.IsAny<PerIsinBlockResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.WriteIsinProgressStep9Async(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.ReleaseIsinProgressAsync(
                It.IsAny<DataLoaderOutput>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.MarkFundFailedAsync(
                "LU0099", "stream-isolate-2", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.MarkFundFailedAsync(
                "LU0001", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
                "surviving funds must not be marked failed");
        });
    }

    [Test]
    public async Task RunAllStreamingAsync_Step10Fails_DoesNotRelease()
    {
        _gateway
            .Setup(x => x.LoadStep1Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeStep1Output(MakeFund("LU0001")));
        _gateway
            .Setup(x => x.LoadStep3Output(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(MakeMacroContext());

        var portfolio = _fixture.Freeze<Mock<IPortfolioConstructorAgent>>();
        portfolio
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("step 10 boom"));
        _sut.Dispose();
        _sut = _fixture.Create<PipelineRunner>();

        await _sut.RunAllStreamingAsync("OPM", "2026-W21", "stream-progress-4");

        Assert.Multiple(() =>
        {
            _gateway.Verify(x => x.ClaimIsinProgressAsync(
                It.IsAny<DataLoaderOutput>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.WriteIsinProgressBlockAsync(
                It.IsAny<PerIsinBlockResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.WriteIsinProgressStep9Async(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _gateway.Verify(x => x.ReleaseIsinProgressAsync(
                It.IsAny<DataLoaderOutput>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    // ───────────────────────── fixtures ─────────────────────────

    private static DataLoaderOutput MakeStep1Output(params FundRecord[] funds) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = "2026-W21",
        Family          = "synthetic",
        RunId           = "test-run",
        ConfigVersion   = "1.0.0",
        Funds           = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        CashAvailableKr = 0m,
        DataQuality     = new DataQuality(),
    };

    private static MacroContext MakeMacroContext() => new()
    {
        GeneratedAt      = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek          = "2026-W21",
        ConfigVersion    = "1.0.0",
        SourceRunIds     = new SourceRunIds
        {
            WeeklySummaryRunId      = "synthetic-ws",
            SubstitutionChainRunId  = "synthetic-sc",
            RotationTargetsRunId    = "synthetic-rt",
        },
        MacroRegime      = MacroRegime.Mixed,
        RegimeConfidence = 0.5m,
        NetMoodInput     = MarketSentiment.Mixed,
        Catalysts        = Array.Empty<Catalyst>(),
        RotationThemes   = Array.Empty<RotationTheme>(),
        Warnings         = null,
    };

    private static FundRecord MakeFund(string isin) => new()
    {
        Isin           = isin,
        Metadata       = new FundMetadata
        {
            Isin                     = isin,
            Name                     = $"Fund {isin}",
            CompanyName              = "TestCo",
            CurrencyCode             = "SEK",
            Category                 = "Globalfond",
            FundType                 = "EQUITY_FUND",
            IsIndexFund              = false,
            ManagedType              = "ACTIVE",
            TotalFee                 = 1.0m,
            ManagementFee            = 0.7m,
            Risk                     = 4,
            Rating                   = 3,
            SharpeRatioStatic        = 1.0m,
            StandardDeviationStatic  = 12.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
            Capital                  = 1_000_000m,
            NumberOfOwners           = 100,
        },
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = null,
    };
}
