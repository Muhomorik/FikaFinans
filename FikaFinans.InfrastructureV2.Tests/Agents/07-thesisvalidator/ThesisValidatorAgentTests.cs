using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Application.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.ThesisValidator;

[TestFixture]
[TestOf(typeof(ThesisValidatorAgent))]
public sealed class ThesisValidatorAgentTests
{
    private IFixture _fixture = null!;
    private Mock<IThesisRefinementLlmClient> _llmMock = null!;
    private ThesisValidatorAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _llmMock = _fixture.Freeze<Mock<IThesisRefinementLlmClient>>();
        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundRecord _, ThesisValidity baseline, CancellationToken _) =>
                ThesisRefinementVerdict.ConfirmBaseline(baseline, "LLM confirmed baseline."));
        _sut = _fixture.Create<ThesisValidatorAgent>();
    }

    #region Matrix-only cases (no LLM)

    [Test]
    public async Task RunInMemoryAsync_StrengthPlusCatalystPlusStrong_ValidNoLlmCall()
    {
        // Arrange
        var fund = MakeFund("LU0001",
            signal: SignalLabel.Strength,
            macro: MacroAlignment.Strong,
            catalyst: MakeCatalyst());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Valid));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
            Assert.That(validated.ThesisRationale, Is.Not.Null.And.Not.Empty);
        });
        _llmMock.Verify(x => x.RefineAsync(
                It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_StrengthOnlyNoCatalystNoMacro_PartialNoLlmCall()
    {
        // Arrange — Strength + no catalyst + Partial macro is the clean
        // "momentum without supporting context" case from the matrix.
        var fund = MakeFund("LU0002",
            signal: SignalLabel.Strength,
            macro: MacroAlignment.Partial,
            catalyst: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Partial));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
        });
        _llmMock.Verify(x => x.RefineAsync(
                It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_StrengthPlusStrongMacroNoCatalyst_ValidNoLlmCall()
    {
        // Arrange
        var fund = MakeFund("LU0003",
            signal: SignalLabel.Strength,
            macro: MacroAlignment.Strong,
            catalyst: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ThesisValidity, Is.EqualTo(ThesisValidity.Valid));
        _llmMock.Verify(x => x.RefineAsync(
                It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_NeutralAnyContext_NotApplicable()
    {
        // Arrange
        var fund = MakeFund("LU0004",
            signal: SignalLabel.Neutral,
            macro: MacroAlignment.Strong,
            catalyst: MakeCatalyst());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
            Assert.That(validated.ThesisRationale, Does.Contain("No directional signal"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_FormingPlusStrongMacro_PartialNoLlmCall()
    {
        // Arrange
        var fund = MakeFund("LU0005",
            signal: SignalLabel.Forming,
            macro: MacroAlignment.Strong,
            catalyst: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ThesisValidity, Is.EqualTo(ThesisValidity.Partial));
    }

    [Test]
    public async Task RunInMemoryAsync_FormingPlusNoneMacro_NotApplicableDefensive()
    {
        // Arrange — defensive case: Forming with None macro can't really occur in
        // v1 (MacroAligner only promotes Neutral → Forming on Strong macro), but
        // if somehow it does, we collapse to NotApplicable.
        var fund = MakeFund("LU0006",
            signal: SignalLabel.Forming,
            macro: MacroAlignment.None,
            catalyst: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
    }

    [Test]
    public async Task RunInMemoryAsync_WeaknessNoCatalystPartialMacro_PartialNoLlmCall()
    {
        // Arrange — Weakness rows that DON'T have catalyst-or-Strong-macro
        // signals don't need LLM refinement.
        var fund = MakeFund("LU0007",
            signal: SignalLabel.Weakness,
            macro: MacroAlignment.Partial,
            catalyst: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ThesisValidity, Is.EqualTo(ThesisValidity.Partial));
        _llmMock.Verify(x => x.RefineAsync(
                It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region LLM-refined cases

    [Test]
    public async Task RunInMemoryAsync_WeaknessPlusCatalystPlusStrongMacro_InvalidLlmConfirms()
    {
        // Arrange — the canonical "thesis broken" case.
        var fund = MakeFund("LU0008",
            signal: SignalLabel.Weakness,
            macro: MacroAlignment.Strong,
            catalyst: MakeCatalyst("Hormuz", ExposureType.Direct));

        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThesisRefinementVerdict
            {
                Validity  = ThesisValidity.Invalid,
                Rationale = "Catalyst still active but price action reversed — thesis broken.",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Invalid));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.LlmRefinement));
            Assert.That(validated.ThesisRationale, Does.Contain("price action reversed"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_WeaknessNoCatalystPlusStrongMacro_PartialLlmConfirms()
    {
        // Arrange — momentum decay against macro support; LLM refines rationale.
        var fund = MakeFund("LU0009",
            signal: SignalLabel.Weakness,
            macro: MacroAlignment.Strong,
            catalyst: null);

        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThesisRefinementVerdict
            {
                Validity  = ThesisValidity.Partial,
                Rationale = "Momentum decay despite Strong macro tailwind.",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Partial));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.LlmRefinement));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_StrengthPlusCatalystPartialMacro_LlmRefines()
    {
        // Arrange — "optional" refinement case.
        var fund = MakeFund("LU0010",
            signal: SignalLabel.Strength,
            macro: MacroAlignment.Partial,
            catalyst: MakeCatalyst("AI capex", ExposureType.Direct));

        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThesisRefinementVerdict
            {
                Validity  = ThesisValidity.Partial,
                Rationale = "AI capex direct exposure but macro only partially aligned.",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Partial));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.LlmRefinement));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_LlmAdjustsByOneStep_Accepted()
    {
        // Arrange — LLM nudges Invalid → Partial. One step is allowed.
        var fund = MakeFund("LU0011",
            signal: SignalLabel.Weakness,
            macro: MacroAlignment.Strong,
            catalyst: MakeCatalyst("Hormuz", ExposureType.Indirect));

        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThesisRefinementVerdict
            {
                Validity  = ThesisValidity.Partial,
                Rationale = "Indirect exposure means weaker contradiction; downgrade to Partial.",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Partial));
        Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.LlmRefinement));
    }

    [Test]
    public async Task RunInMemoryAsync_LlmJumpsTwoSteps_OverriddenAndWarned()
    {
        // Arrange — baseline is Invalid (Weakness + catalyst + Strong macro);
        // LLM tries to jump to Valid. That's two steps — override and keep
        // baseline.
        var fund = MakeFund("LU0012",
            signal: SignalLabel.Weakness,
            macro: MacroAlignment.Strong,
            catalyst: MakeCatalyst("Hormuz", ExposureType.Direct));

        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThesisRefinementVerdict
            {
                Validity  = ThesisValidity.Valid,
                Rationale = "LLM hallucinated a Valid label.",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Invalid));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("LU0012"));
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("Invalid"));
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("Valid"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_LlmJumpsToNotApplicable_OverriddenAndWarned()
    {
        // Arrange — LLM cannot drag an actionable signal into NotApplicable.
        var fund = MakeFund("LU0013",
            signal: SignalLabel.Weakness,
            macro: MacroAlignment.Partial,
            catalyst: MakeCatalyst("Hormuz", ExposureType.Direct));

        _llmMock
            .Setup(x => x.RefineAsync(
                It.IsAny<FundRecord>(),
                It.IsAny<ThesisValidity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThesisRefinementVerdict
            {
                Validity  = ThesisValidity.NotApplicable,
                Rationale = "LLM reaches for an inappropriate label.",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.Invalid));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
        });
    }

    #endregion

    #region Failure modes

    [Test]
    public async Task RunInMemoryAsync_NullSignal_NotApplicableAndWarn()
    {
        // Arrange
        var fund = MakeFund("LU0014",
            signal: null,
            macro: MacroAlignment.Strong,
            catalyst: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
            Assert.That(validated.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("LU0014"));
        });
        _llmMock.Verify(x => x.RefineAsync(
                It.IsAny<FundRecord>(), It.IsAny<ThesisValidity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Append-only

    [Test]
    public async Task RunInMemoryAsync_PreservesUpstreamFields()
    {
        // Arrange
        var fund = MakeFund("LU0015",
            signal: SignalLabel.Strength,
            macro: MacroAlignment.Strong,
            catalyst: MakeCatalyst());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var validated = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validated.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(validated.NavBuckets, Is.SameAs(fund.NavBuckets));
            Assert.That(validated.Metrics, Is.Not.Null);
            Assert.That(validated.Signal, Is.EqualTo(fund.Signal));
            Assert.That(validated.RuleFired, Is.EqualTo(fund.RuleFired));
            Assert.That(validated.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(validated.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(validated.PromotedToForming, Is.EqualTo(fund.PromotedToForming));
            Assert.That(validated.Catalyst, Is.SameAs(fund.Catalyst));
        });
    }

    #endregion

    #region Disk happy path

    [Test]
    public async Task RunAsync_RealHappyPathFixture_ProducesThesisEnrichedJson()
    {
        // Arrange — cascade fixtures up to step 06, run step 07.
        const string runId = "test-happypath";
        await EnsureCatalystTaggerFixtureExistsAsync(runId);

        var sut = _fixture.Create<ThesisValidatorAgent>();

        // Act
        var result = await sut.RunAsync("2026-W18", runId);

        // Assert
        var outPath = Paths.ThesisValidatorOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Is.Not.Empty);
            Assert.That(roundTripped!.Funds.All(f => f.ThesisValidity is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => f.ThesisMethod is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => !string.IsNullOrEmpty(f.ThesisRationale)), Is.True);
            // Step 06 field preserved (catalyst can be null per fund).
            Assert.That(roundTripped.Funds.All(f => f.MatchedTheme is not null), Is.True);
        });

        var json = await File.ReadAllTextAsync(outPath);
        Assert.That(json, Does.Contain("\"thesis_validity\":"));
        Assert.That(json, Does.Contain("\"thesis_method\":"));
        Assert.That(json, Does.Contain("\"thesis_rationale\":"));
    }

    private static async Task EnsureCatalystTaggerFixtureExistsAsync(string runId)
    {
        // Cascade — run prior steps if their outputs are missing. Mirrors the
        // pattern in CatalystTaggerAgentTests so tests can run from a clean
        // stepOutputs/ folder.
        var step6Path = Paths.CatalystTaggerOutput("2026-W18", runId);
        var step5Path = Paths.MacroAlignerOutput("2026-W18", runId);
        var step3Path = Paths.MacroAnalystOutput("2026-W18", runId);

        if (File.Exists(step6Path) && File.Exists(step5Path) && File.Exists(step3Path))
            return;

        var step4Path = Paths.SignalScorerOutput("2026-W18", runId);
        if (!File.Exists(step4Path))
        {
            var step2Path = Paths.MetricsCalculatorOutput("2026-W18", runId);
            if (!File.Exists(step2Path))
            {
                var step1Path = Paths.DataLoaderOutput("2026-W18", runId);
                if (!File.Exists(step1Path))
                {
                    new FikaFinans.Infrastructure.Pipeline.Agents.DataLoaderAgent(new TestPathsService(), FikaFinans.InfrastructureV2.Tests.Storage.InMemoryPositionsRepository.SeededFromCsv(Paths.PositionsCsvAbs))
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
        MacroAlignment macro,
        FundCatalyst? catalyst) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = MakeMetrics(),
        Signal         = signal,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = macro,
        MatchedTheme      = new MatchedTheme
        {
            Id          = "rot_theme_x",
            Label       = "Some theme",
            MatchMethod = MatchMethod.DirectCategory,
        },
        PromotedToForming = false,
        PromotionReason   = null,
        Catalyst          = catalyst,
    };

    private static FundCatalyst MakeCatalyst(string name = "Hormuz", ExposureType exposure = ExposureType.Direct) => new()
    {
        Name         = name,
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
