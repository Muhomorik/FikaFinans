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
    public async Task RunPerIsinBlockAsync_ReturnsEnrichedUniverseWithSameFundCount()
    {
        var step1 = MakeStep1Output(MakeFund("LU0001"), MakeFund("LU0002"), MakeFund("LU0003"));
        var macro = MakeMacroContext();

        var result = await _sut.RunPerIsinBlockAsync(
            step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
            maxConcurrent: 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Has.Count.EqualTo(3));
            Assert.That(result.IsoWeek, Is.EqualTo(step1.IsoWeek));
            Assert.That(result.Family, Is.EqualTo(step1.Family));
            Assert.That(result.RunId, Is.EqualTo(step1.RunId));
        });
    }

    [Test]
    public async Task RunPerIsinBlockAsync_FoldsPerFundWarningsIntoDataQuality()
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

        Assert.That(result.DataQuality.Warnings, Has.Member("warn-LU0001"));
        Assert.That(result.DataQuality.Warnings, Has.Member("warn-LU0002"));
    }

    [Test]
    public void RunPerIsinBlockAsync_StepThrows_EmitsFailedWithIsinAndPropagates()
    {
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

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sut.RunPerIsinBlockAsync(
                step1, macro, MetricsCalculatorConfig.Default, SignalScorerConfig.Default,
                maxConcurrent: 1));

        var failed = observed.SingleOrDefault(e => e.Kind == StepEventKind.Failed);
        Assert.That(failed, Is.Not.Null);
        Assert.That(failed!.Step, Is.EqualTo(StepId.ThesisValidator));
        Assert.That(failed.Isin?.Value, Is.EqualTo(failingIsin));
        Assert.That(failed.Message, Does.Contain("LLM exploded"));
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
