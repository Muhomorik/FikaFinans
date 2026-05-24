using System.Reactive.Linq;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(PipelineRunner))]
public sealed class PipelineRunnerTests
{
    private IFixture _fixture = null!;
    private Mock<IMacroAnalystAgent> _macroAnalyst = null!;
    private Mock<IMacroAlignerAgent> _macroAligner = null!;
    private Mock<ICatalystTaggerAgent> _catalyst = null!;
    private Mock<IThesisValidatorAgent> _thesis = null!;
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

        _catalyst = _fixture.Freeze<Mock<ICatalystTaggerAgent>>();
        _catalyst
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);

        _thesis = _fixture.Freeze<Mock<IThesisValidatorAgent>>();
        _thesis
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);

        _enricher = _fixture.Freeze<Mock<IUniverseEnricherAgent>>();
        _enricher
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DataLoaderOutput)!);

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
}
