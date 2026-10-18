using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.ThesisValidator;

/// <summary>
/// The domain-input <c>RunInMemoryAsync</c> — the call the per-fund step makes, step 6's
/// <c>ICatalystTaggerAgent.RunInMemoryAsync</c> twin. The matrix and the one-step rule
/// themselves are covered in <see cref="ThesisValidatorAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(ThesisValidatorAgent))]
public sealed class ThesisValidatorAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W18");
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;
    private Mock<IThesisRefinementLlmClient> _llmMock = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);

        _llmMock = _fixture.Freeze<Mock<IThesisRefinementLlmClient>>();
        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundRecord _, ThesisValidity baseline, CancellationToken _) =>
                ThesisRefinementVerdict.ConfirmBaseline(baseline, "LLM confirmed baseline."));
    }

    private ThesisValidatorAgent CreateSut() => _fixture.Create<ThesisValidatorAgent>();

    [Test]
    public async Task RunInMemoryAsync_TwoFunds_ValidatesEachLikeProcessFundAsync()
    {
        // Arrange
        var sut = CreateSut();
        var strong = MakeFund("LU7001", SignalLabel.Strength, MacroAlignment.Strong, MakeCatalyst());
        var neutral = MakeFund("LU7002", SignalLabel.Neutral, MacroAlignment.None, null);

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, [strong, neutral]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU7001", "LU7002" }));
            Assert.That(result.Funds[0].ThesisValidity, Is.EqualTo(ThesisValidity.Valid));
            Assert.That(result.Funds[0].ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
            Assert.That(result.Funds[1].ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_OnlyMatrixRows_CallsNoLlm()
    {
        // Arrange — unlike step 6, a clean matrix row never reaches the LLM.
        var sut = CreateSut();

        // Act
        await sut.RunInMemoryAsync(Family, Week, RunId,
        [
            MakeFund("LU7003", SignalLabel.Strength, MacroAlignment.Strong, null),
            MakeFund("LU7004", SignalLabel.Neutral, MacroAlignment.Strong, MakeCatalyst()),
        ]);

        // Assert
        _llmMock.Verify(x => x.RefineAsync(
            It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_ConflictingRow_CallsLlmOnceWithBaseline()
    {
        // Arrange — Weakness with a firing catalyst: the canonical exit case.
        var sut = CreateSut();
        var fund = MakeFund("LU7005", SignalLabel.Weakness, MacroAlignment.Strong, MakeCatalyst());

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, [fund]);

        // Assert
        Assert.That(result.Funds.Single().ThesisMethod, Is.EqualTo(ThesisMethod.LlmRefinement));
        _llmMock.Verify(x => x.RefineAsync(
            It.Is<FundRecord>(f => f.Isin.Value == "LU7005"), ThesisValidity.Invalid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunInMemoryAsync_ValidatedFund_KeepsEveryEarlierField()
    {
        // Arrange — append-only: step 7 adds the thesis, it never rewrites what came before.
        var sut = CreateSut();
        var fund = MakeFund("LU7006", SignalLabel.Weakness, MacroAlignment.Strong, MakeCatalyst());

        // Act
        var validated = (await sut.RunInMemoryAsync(Family, Week, RunId, [fund])).Funds.Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(validated.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(validated.CriteriaEvaluation, Is.SameAs(fund.CriteriaEvaluation));
            Assert.That(validated.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(validated.Catalyst, Is.SameAs(fund.Catalyst));
            Assert.That(validated.Signal, Is.EqualTo(fund.Signal));
            Assert.That(validated.RuleFired, Is.EqualTo(fund.RuleFired));
            Assert.That(validated.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(validated.PromotedToForming, Is.EqualTo(fund.PromotedToForming));
            Assert.That(validated.Layer, Is.EqualTo(fund.Layer));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_FundWarnings_LandInDataQualityWarnings()
    {
        // Arrange — a null signal is a per-fund warning, not a failure.
        var sut = CreateSut();

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId,
            [MakeFund("LU7007", null, MacroAlignment.None, null)]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Single().ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
            Assert.That(result.DataQuality.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.DataQuality.Warnings[0], Does.Contain("LU7007"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_Always_StampsEnvelopeFromArguments()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, Array.Empty<FundRecord>());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Family, Is.EqualTo("schroder"));
            Assert.That(result.IsoWeek, Is.EqualTo("2026-W18"));
            Assert.That(result.RunId, Is.EqualTo(RunId));
            Assert.That(result.Funds, Is.Empty);
            Assert.That(result.DataQuality.Warnings, Is.Empty);
        });
    }

    [Test]
    public void RunInMemoryAsync_NullFunds_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.RunInMemoryAsync(Family, Week, RunId, null!));
    }

    // ────────────────────────── helpers ──────────────────────────

    private static FundCatalyst MakeCatalyst() => new()
    {
        Name         = "Hormuz disruption",
        Intensity    = Intensity.High,
        WeeksActive  = 8,
        ExposureType = ExposureType.Direct,
        Rationale    = "synthetic",
    };

    private static FundRecord MakeFund(
        string isin, SignalLabel? signal, MacroAlignment macro, FundCatalyst? catalyst) => new()
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
        CurrentlyHeld = true,
        Layer = FundLayer.Active,
        Signal = signal,
        RuleFired = "synthetic_rule",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment = macro,
        MatchedTheme = new MatchedTheme
        {
            Id          = "rot_theme_energy",
            Label       = "Integrated oil + inflation hedges",
            MatchMethod = MatchMethod.DirectCategory,
        },
        PromotedToForming = false,
        Catalyst = catalyst,
    };
}
