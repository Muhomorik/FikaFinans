using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Portfolio;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.Recommender;

[TestFixture]
[TestOf(typeof(RecommenderAgent))]
public sealed class RecommenderAgentTests
{
    private IFixture _fixture = null!;
    private RecommenderAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _sut = _fixture.Create<RecommenderAgent>();
    }

    #region Strength path

    [Test]
    public void RunInMemory_StrengthValidDirectCatalyst_CatalystEntry()
    {
        // Arrange
        var fund = MakeFund("LU0001",
            signal:   SignalLabel.Strength,
            thesis:   ThesisValidity.Valid,
            catalyst: MakeCatalyst(ExposureType.Direct),
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.CatalystEntry));
            Assert.That(enriched.RecommendationReason, Does.Contain("Strength"));
            Assert.That(enriched.RecommendationReason, Does.Contain("Direct"));
        });
    }

    [Test]
    public void RunInMemory_StrengthValidIndirectCatalyst_MomentumEntry()
    {
        // Arrange — Indirect exposure does NOT promote to CatalystEntry.
        var fund = MakeFund("LU0002",
            signal:   SignalLabel.Strength,
            thesis:   ThesisValidity.Valid,
            catalyst: MakeCatalyst(ExposureType.Indirect),
            held:     true);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.MomentumEntry));
            Assert.That(enriched.RecommendationReason, Does.Contain("Indirect"));
        });
    }

    [Test]
    public void RunInMemory_StrengthPartialNoCatalyst_MomentumEntry()
    {
        // Arrange
        var fund = MakeFund("LU0003",
            signal:   SignalLabel.Strength,
            thesis:   ThesisValidity.Partial,
            catalyst: null,
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.MomentumEntry));
            Assert.That(enriched.RecommendationReason, Does.Contain("no catalyst"));
        });
    }

    [Test]
    public void RunInMemory_StrengthPartialDirectCatalyst_CatalystEntry()
    {
        // Arrange — Partial thesis still counts as CatalystEntry when the
        // catalyst exposure is Direct. The thesis modulation happens later
        // (UniverseEnricher conviction scoring).
        var fund = MakeFund("LU0004",
            signal:   SignalLabel.Strength,
            thesis:   ThesisValidity.Partial,
            catalyst: MakeCatalyst(ExposureType.Direct),
            held:     true);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.CatalystEntry));
    }

    #endregion

    #region Weakness path

    [Test]
    public void RunInMemory_WeaknessInvalidDirectCatalyst_ThesisExit()
    {
        // Arrange — the canonical exit case: thesis broken even though the
        // catalyst is still firing externally.
        var fund = MakeFund("LU0005",
            signal:   SignalLabel.Weakness,
            thesis:   ThesisValidity.Invalid,
            catalyst: MakeCatalyst(ExposureType.Direct),
            held:     true);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.ThesisExit));
            Assert.That(enriched.RecommendationReason, Does.Contain("Invalid"));
        });
    }

    [Test]
    public void RunInMemory_WeaknessPartialNoCatalyst_MomentumExit()
    {
        // Arrange
        var fund = MakeFund("LU0006",
            signal:   SignalLabel.Weakness,
            thesis:   ThesisValidity.Partial,
            catalyst: null,
            held:     true);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.MomentumExit));
            Assert.That(enriched.RecommendationReason, Does.Contain("Partial"));
        });
    }

    [Test]
    public void RunInMemory_WeaknessInvalidNotHeld_StillThesisExit()
    {
        // Arrange — even if the fund isn't held, we honestly emit ThesisExit;
        // PortfolioConstructor (step 10) collapses it to NoOp downstream.
        var fund = MakeFund("LU0007",
            signal:   SignalLabel.Weakness,
            thesis:   ThesisValidity.Invalid,
            catalyst: MakeCatalyst(ExposureType.Direct),
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.ThesisExit));
    }

    #endregion

    #region Forming path

    [Test]
    public void RunInMemory_FormingHeld_Maintain()
    {
        // Arrange
        var fund = MakeFund("LU0008",
            signal:   SignalLabel.Forming,
            thesis:   ThesisValidity.Partial,
            catalyst: null,
            held:     true);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.Maintain));
    }

    [Test]
    public void RunInMemory_FormingNotHeld_Skip()
    {
        // Arrange
        var fund = MakeFund("LU0009",
            signal:   SignalLabel.Forming,
            thesis:   ThesisValidity.Partial,
            catalyst: null,
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.Skip));
    }

    #endregion

    #region Neutral path

    [Test]
    public void RunInMemory_NeutralHeld_Maintain()
    {
        // Arrange
        var fund = MakeFund("LU0010",
            signal:   SignalLabel.Neutral,
            thesis:   ThesisValidity.NotApplicable,
            catalyst: null,
            held:     true);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.Maintain));
            Assert.That(enriched.RecommendationReason, Does.Contain("Neutral"));
        });
    }

    [Test]
    public void RunInMemory_NeutralNotHeld_Skip()
    {
        // Arrange
        var fund = MakeFund("LU0011",
            signal:   SignalLabel.Neutral,
            thesis:   ThesisValidity.NotApplicable,
            catalyst: null,
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.Skip));
    }

    #endregion

    #region Failure modes

    [Test]
    public void RunInMemory_NullSignal_SkipAndWarn()
    {
        // Arrange
        var fund = MakeFund("LU0012",
            signal:   null,
            thesis:   ThesisValidity.NotApplicable,
            catalyst: null,
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Recommendation, Is.EqualTo(Recommendation.Skip));
            Assert.That(enriched.RecommendationReason, Is.EqualTo("no_signal_no_action"));
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("LU0012"));
        });
    }

    [Test]
    public void RunInMemory_NullThesisStrengthDirect_StillCatalystEntry()
    {
        // Arrange — defensive: if thesis_validity is somehow null but signal
        // is Strength + Direct catalyst, the recommendation is still
        // CatalystEntry. The mapping treats null thesis as NotApplicable.
        var fund = MakeFund("LU0013",
            signal:   SignalLabel.Strength,
            thesis:   null,
            catalyst: MakeCatalyst(ExposureType.Direct),
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.CatalystEntry));
    }

    #endregion

    #region Append-only

    [Test]
    public void RunInMemory_PreservesUpstreamFields()
    {
        // Arrange
        var fund = MakeFund("LU0014",
            signal:   SignalLabel.Strength,
            thesis:   ThesisValidity.Valid,
            catalyst: MakeCatalyst(ExposureType.Direct),
            held:     false);

        // Act
        var result = _sut.RunInMemory(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(enriched.NavBuckets, Is.SameAs(fund.NavBuckets));
            Assert.That(enriched.Metrics, Is.SameAs(fund.Metrics));
            Assert.That(enriched.Signal, Is.EqualTo(fund.Signal));
            Assert.That(enriched.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(enriched.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(enriched.Catalyst, Is.SameAs(fund.Catalyst));
            Assert.That(enriched.ThesisValidity, Is.EqualTo(fund.ThesisValidity));
            Assert.That(enriched.ThesisRationale, Is.EqualTo(fund.ThesisRationale));
            Assert.That(enriched.ThesisMethod, Is.EqualTo(fund.ThesisMethod));
        });
    }

    #endregion

    #region Disk happy path

    [Test]
    public async Task Run_RealHappyPathFixture_ProducesRecommendationEnrichedJson()
    {
        // Arrange — cascade fixtures up to step 07, run step 08.
        const string runId = "test-happypath";
        await EnsureThesisValidatorFixtureExistsAsync(runId);

        var sut = _fixture.Create<RecommenderAgent>();

        // Act
        var result = sut.Run("2026-W18", runId);

        // Assert
        var outPath = Paths.RecommenderOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Is.Not.Empty);
            Assert.That(roundTripped!.Funds.All(f => f.Recommendation is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => !string.IsNullOrEmpty(f.RecommendationReason)), Is.True);
            // Step 07 fields preserved.
            Assert.That(roundTripped.Funds.All(f => f.ThesisValidity is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => f.ThesisMethod is not null), Is.True);
        });

        var json = await File.ReadAllTextAsync(outPath);
        Assert.That(json, Does.Contain("\"recommendation\":"));
        Assert.That(json, Does.Contain("\"recommendation_reason\":"));
    }

    private static async Task EnsureThesisValidatorFixtureExistsAsync(string runId)
    {
        // Cascade — run prior steps if their outputs are missing. Mirrors the
        // pattern in ThesisValidatorAgentTests so tests can run from a clean
        // stepOutputs/ folder.
        var step7Path = Paths.ThesisValidatorOutput("2026-W18", runId);
        var step6Path = Paths.CatalystTaggerOutput("2026-W18", runId);
        var step5Path = Paths.MacroAlignerOutput("2026-W18", runId);
        var step3Path = Paths.MacroAnalystOutput("2026-W18", runId);

        if (File.Exists(step7Path)) return;

        var step4Path = Paths.SignalScorerOutput("2026-W18", runId);
        if (!File.Exists(step4Path))
        {
            var step2Path = Paths.MetricsCalculatorOutput("2026-W18", runId);
            if (!File.Exists(step2Path))
            {
                var step1Path = Paths.DataLoaderOutput("2026-W18", runId);
                if (!File.Exists(step1Path))
                {
                    new FikaFinans.Infrastructure.Pipeline.Agents.DataLoaderAgent(new TestPathsService())
                        .Run("schroder", "2026-W18", runId);
                }
                new FikaFinans.Infrastructure.Pipeline.Agents.MetricsCalculatorAgent(new TestPathsService())
                    .Run("2026-W18", runId);
            }
            new FikaFinans.Infrastructure.Pipeline.Agents.SignalScorerAgent(new TestPathsService())
                .Run("2026-W18", runId);
        }

        if (!File.Exists(step3Path))
        {
            var synthetic = MakeSyntheticMacroContext("2026-W18");
            Directory.CreateDirectory(Path.GetDirectoryName(step3Path)!);
            await File.WriteAllTextAsync(step3Path,
                JsonSerializer.Serialize(synthetic, JsonOptions.Default));
        }

        if (!File.Exists(step5Path))
        {
            var alignLlm = new Mock<IThemeAdjacencyLlmClient>();
            alignLlm
                .Setup(x => x.ClassifyAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<RotationTheme>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ThemeAdjacencyVerdict.NoneVerdict);
            await new MacroAlignerAgent(new TestPathsService(), alignLlm.Object).RunAsync("2026-W18", runId);
        }

        if (!File.Exists(step6Path))
        {
            var taggerLlm = new Mock<IFundCatalystLlmClient>();
            taggerLlm
                .Setup(x => x.ClassifyAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<Catalyst>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CatalystExposureClassification>());
            await new CatalystTaggerAgent(new TestPathsService(), taggerLlm.Object).RunAsync("2026-W18", runId);
        }

        if (!File.Exists(step7Path))
        {
            var thesisLlm = new Mock<IThesisRefinementLlmClient>();
            thesisLlm
                .Setup(x => x.RefineAsync(
                    It.IsAny<FundRecord>(),
                    It.IsAny<ThesisValidity>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((FundRecord _, ThesisValidity baseline, CancellationToken _) =>
                    ThesisRefinementVerdict.ConfirmBaseline(baseline, "Cascade-stub LLM confirmation."));
            await new ThesisValidatorAgent(new TestPathsService(), thesisLlm.Object).RunAsync("2026-W18", runId);
        }
    }

    private static MacroContext MakeSyntheticMacroContext(string isoWeek) => new()
    {
        GeneratedAt      = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek          = isoWeek,
        ConfigVersion    = "1.0.0",
        SourceRunIds     = new SourceRunIds
        {
            WeeklySummaryRunId      = "synthetic-ws",
            SubstitutionChainRunId  = "synthetic-sc",
            RotationTargetsRunId    = "synthetic-rt",
        },
        MacroRegime      = MacroRegime.Mixed,
        RegimeConfidence = 0.7m,
        NetMoodInput     = MarketSentiment.Mixed,
        Catalysts        =
        [
            new Catalyst
            {
                Name               = "Hormuz disruption",
                Intensity          = Intensity.High,
                WeeksActive        = 8,
                AffectedCategories = ["Branschfond, Energi", "Råvarufond"],
                Rationale          = "synthetic",
            },
        ],
        RotationThemes   =
        [
            new RotationTheme
            {
                Id                 = $"rot_theme_energy_{isoWeek}",
                Label              = "Integrated oil + inflation hedges",
                SignalStrength     = SignalStrength.Strong,
                AffectedCategories = ["Branschfond, Energi", "Råvarufond"],
                Rationale          = "synthetic",
                SourceChain        = null,
            },
        ],
        Warnings         = null,
    };

    #endregion

    #region Helpers

    private static FundRecord MakeFund(
        string isin,
        SignalLabel? signal,
        ThesisValidity? thesis,
        FundCatalyst? catalyst,
        bool held) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = held,
        CurrentValueKr = held ? 10_000m : null,
        CostBasisKr    = held ? 9_000m : null,
        Layer          = FundLayer.Active,
        Metrics        = MakeMetrics(),
        Signal         = signal,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = MacroAlignment.Strong,
        MatchedTheme      = new MatchedTheme
        {
            Id          = "rot_theme_x",
            Label       = "Some theme",
            MatchMethod = MatchMethod.DirectCategory,
        },
        PromotedToForming = false,
        PromotionReason   = null,
        Catalyst          = catalyst,
        ThesisValidity    = thesis,
        ThesisRationale   = thesis is null ? null : "Upstream rationale.",
        ThesisMethod      = thesis is null ? null : ThesisMethod.Matrix,
    };

    private static FundCatalyst MakeCatalyst(ExposureType exposure) => new()
    {
        Name         = "Hormuz",
        Intensity    = Intensity.High,
        WeeksActive  = 8,
        ExposureType = exposure,
        Rationale    = "synthetic",
    };

    private static FundMetadata MakeMetadata(string isin) => new()
    {
        Isin                     = isin,
        Name                     = $"Test Fund {isin}",
        CompanyName              = "TestCo",
        CurrencyCode             = "SEK",
        Category                 = "Branschfond, Energi",
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
    };

    private static Metrics MakeMetrics() => new()
    {
        WindowsPositiveCount = 3,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = 0m,
        AnnVolatility2wPct   = 12m,
        Sharpe2w             = 1.2m,
        Sharpe12w            = 1.5m,
        Sharpe1y             = 1.0m,
        AnnVolatility12wPct  = 14m,
        AnnVolatility1yPct   = 16m,
        Return12wCompoundPct = 6m,
        Return1yCompoundPct  = 12m,
        MaxDrawdown12wPct    = -4m,
        MaxDrawdown1yPct     = -8m,
        DataQuality          = new MetricsDataQuality(),
    };

    private static DataLoaderOutput MakeOutput(params FundRecord[] funds) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = "2026-W18",
        Family          = "synthetic",
        RunId           = "test-run",
        ConfigVersion   = "1.0.0",
        Funds           = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        CashAvailableKr = 0m,
        DataQuality     = new DataQuality(),
    };

    #endregion
}
