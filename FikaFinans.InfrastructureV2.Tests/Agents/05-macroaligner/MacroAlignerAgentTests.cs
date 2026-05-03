using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;
using FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

[TestFixture]
[TestOf(typeof(MacroAlignerAgent))]
public sealed class MacroAlignerAgentTests
{
    private IFixture _fixture = null!;
    private Mock<IThemeAdjacencyLlmClient> _llmMock = null!;
    private MacroAlignerAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _llmMock = _fixture.Freeze<Mock<IThemeAdjacencyLlmClient>>();
        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThemeAdjacencyVerdict.NoneVerdict);
        _sut = _fixture.Create<MacroAlignerAgent>();
    }

    #region Direct match

    [Test]
    public async Task RunInMemoryAsync_DirectMatchStrongTheme_AlignmentStrongNoLlmCall()
    {
        // Arrange
        var theme = MakeTheme("rot_theme_energy", "Oil + inflation hedges",
            SignalStrength.Strong, ["Branschfond, Energi"]);
        var fund = MakeFund("LU0001", category: "Branschfond, Energi", signal: SignalLabel.Weakness);
        var input = MakeOutput(fund);
        var macro = MakeMacro([theme]);

        // Act
        var result = await _sut.RunInMemoryAsync(input, macro);

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Strong));
            Assert.That(aligned.MatchedTheme!.Id, Is.EqualTo("rot_theme_energy"));
            Assert.That(aligned.MatchedTheme.MatchMethod, Is.EqualTo(MatchMethod.DirectCategory));
            Assert.That(aligned.PromotedToForming, Is.False);
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Weakness));
        });
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RotationTheme>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_DirectMatchModerateTheme_AlignmentPartial()
    {
        // Arrange
        var theme = MakeTheme("rot_theme_energy", "Oil hedges",
            SignalStrength.Moderate, ["Branschfond, Energi"]);
        var fund = MakeFund("LU0002", category: "Branschfond, Energi", signal: SignalLabel.Strength);
        var input = MakeOutput(fund);
        var macro = MakeMacro([theme]);

        // Act
        var result = await _sut.RunInMemoryAsync(input, macro);

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Partial));
            Assert.That(aligned.MatchedTheme!.MatchMethod, Is.EqualTo(MatchMethod.DirectCategory));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_MultipleDirectMatches_PicksHighestStrengthThenLongestList()
    {
        // Arrange — two themes match the fund's category. Strong should beat
        // Moderate even though the Moderate one has a longer list.
        var strong = MakeTheme("rot_strong", "Strong theme",
            SignalStrength.Strong, ["Branschfond, Teknik"]);
        var moderate = MakeTheme("rot_mod", "Moderate theme",
            SignalStrength.Moderate, ["Branschfond, Teknik", "USA-fond", "Globalfond"]);
        var fund = MakeFund("LU0003", category: "Branschfond, Teknik", signal: SignalLabel.Strength);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([moderate, strong]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.That(aligned.MatchedTheme!.Id, Is.EqualTo("rot_strong"));
        Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Strong));
    }

    [Test]
    public async Task RunInMemoryAsync_TiedStrengthMatches_PicksLongerAffectedCategoriesList()
    {
        // Arrange — both Moderate; the longer list (more specific) should win.
        var shortTheme = MakeTheme("rot_short", "Short list",
            SignalStrength.Moderate, ["Globalfond"]);
        var longTheme = MakeTheme("rot_long", "Longer list",
            SignalStrength.Moderate, ["Globalfond", "USA-fond"]);
        var fund = MakeFund("LU0004", category: "Globalfond", signal: SignalLabel.Neutral);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([shortTheme, longTheme]));

        // Assert
        Assert.That(result.Funds.Single().MatchedTheme!.Id, Is.EqualTo("rot_long"));
    }

    #endregion

    #region LLM adjacency

    [Test]
    public async Task RunInMemoryAsync_NoDirectMatch_LlmFindsAdjacency_PartialLlmAdjacency()
    {
        // Arrange
        var theme = MakeTheme("rot_asia", "Asian domestic activity",
            SignalStrength.Moderate, ["Asien-fond"]);
        var fund = MakeFund("LU0005", category: "Tillväxtmarknader", signal: SignalLabel.Strength);

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.Is<string>(c => c == "Tillväxtmarknader"),
                It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThemeAdjacencyVerdict
            {
                Alignment = MacroAlignment.Partial,
                ThemeId   = "rot_asia",
                Rationale = "EM has overlap with Asia",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Partial));
            Assert.That(aligned.MatchedTheme!.Id, Is.EqualTo("rot_asia"));
            Assert.That(aligned.MatchedTheme.MatchMethod, Is.EqualTo(MatchMethod.LlmAdjacency));
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Strength));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_NoDirectMatch_LlmReturnsNone_AlignmentNone()
    {
        // Arrange
        var theme = MakeTheme("rot_x", "Some theme",
            SignalStrength.Moderate, ["Räntefond, Företag"]);
        var fund = MakeFund("LU0006", category: "Tillväxtmarknader, Nya", signal: SignalLabel.Neutral);

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThemeAdjacencyVerdict.NoneVerdict);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.None));
            Assert.That(aligned.MatchedTheme!.MatchMethod, Is.EqualTo(MatchMethod.None));
            Assert.That(aligned.MatchedTheme.Id, Is.Null);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_LlmReturnsUnknownThemeId_FallsBackToNoneAndWarns()
    {
        // Arrange
        var theme = MakeTheme("rot_real", "Real theme",
            SignalStrength.Moderate, ["Räntefond, Företag"]);
        var fund = MakeFund("LU0007", category: "Tillväxtmarknader", signal: SignalLabel.Neutral);

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RotationTheme>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThemeAdjacencyVerdict
            {
                Alignment = MacroAlignment.Partial,
                ThemeId   = "rot_does_not_exist",
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.None));
            Assert.That(result.DataQuality.Warnings,
                Has.Some.Contains("rot_does_not_exist"));
        });
    }

    #endregion

    #region Promotion

    [Test]
    public async Task RunInMemoryAsync_NeutralPlusStrongAlignmentPlusMissingOne_PromotesToForming()
    {
        // Arrange
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Strong, ["Branschfond, Energi"]);
        var fund = MakeFund(
            "LU0008",
            category: "Branschfond, Energi",
            signal: SignalLabel.Neutral,
            missingForUpgrade: "sharpe_12w_above_threshold");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Forming));
            Assert.That(aligned.PromotedToForming, Is.True);
            Assert.That(aligned.PromotionReason, Does.Contain("Strong macro alignment"));
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Strong));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_NeutralPlusStrongAlignmentButNoMissingForUpgrade_NoPromotion()
    {
        // Arrange — missing_for_upgrade is null (means 0 or 2+ criteria missing,
        // not exactly one — only the "1 missing" case promotes per contract).
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Strong, ["Globalfond"]);
        var fund = MakeFund(
            "LU0009",
            category: "Globalfond",
            signal: SignalLabel.Neutral,
            missingForUpgrade: null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Neutral));
            Assert.That(aligned.PromotedToForming, Is.False);
            Assert.That(aligned.PromotionReason, Is.Null);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_StrengthPlusStrongAlignment_NoPromotionStaysStrength()
    {
        // Arrange
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Strong, ["Branschfond, Teknik"]);
        var fund = MakeFund(
            "LU0010",
            category: "Branschfond, Teknik",
            signal: SignalLabel.Strength,
            missingForUpgrade: "sharpe_12w_above_threshold");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Strength));
            Assert.That(aligned.PromotedToForming, Is.False);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_WeaknessPlusStrongAlignment_NoPromotionStaysWeakness()
    {
        // Arrange
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Strong, ["Branschfond, Energi"]);
        var fund = MakeFund(
            "LU0011",
            category: "Branschfond, Energi",
            signal: SignalLabel.Weakness);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Weakness));
            Assert.That(aligned.PromotedToForming, Is.False);
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Strong));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_NeutralPlusPartialAlignmentPlusMissingOne_NoPromotion()
    {
        // Arrange — only Strong alignment promotes; Partial does not.
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Moderate, ["Globalfond"]);
        var fund = MakeFund(
            "LU0012",
            category: "Globalfond",
            signal: SignalLabel.Neutral,
            missingForUpgrade: "drawdown_above_threshold");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.Partial));
            Assert.That(aligned.Signal, Is.EqualTo(SignalLabel.Neutral));
            Assert.That(aligned.PromotedToForming, Is.False);
        });
    }

    #endregion

    #region Failure modes

    [Test]
    public async Task RunInMemoryAsync_NoActiveThemes_AllFundsAlignmentNoneNoLlmCall()
    {
        // Arrange
        var f1 = MakeFund("LU0013", category: "Branschfond, Energi", signal: SignalLabel.Strength);
        var f2 = MakeFund("LU0014", category: "Globalfond", signal: SignalLabel.Neutral);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(f1, f2), MakeMacro([]));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.All(f => f.MacroAlignment == MacroAlignment.None), Is.True);
            Assert.That(result.Funds.All(f => f.MatchedTheme!.MatchMethod == MatchMethod.None), Is.True);
        });
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RotationTheme>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_FundCategoryEmpty_AlignmentNoneAndWarn()
    {
        // Arrange
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Strong, ["Branschfond, Energi"]);
        var fund = MakeFund("LU0015", category: "", signal: SignalLabel.Strength);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.MacroAlignment, Is.EqualTo(MacroAlignment.None));
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("LU0015"));
        });
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RotationTheme>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Append-only

    [Test]
    public async Task RunInMemoryAsync_PreservesUpstreamFields()
    {
        // Arrange — fund carries a full chain of step 02 + step 04 fields.
        var theme = MakeTheme("rot_t", "Theme",
            SignalStrength.Strong, ["Branschfond, Energi"]);
        var fund = MakeFund("LU0016", category: "Branschfond, Energi", signal: SignalLabel.Strength);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([theme]));

        // Assert
        var aligned = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(aligned.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(aligned.NavBuckets, Is.SameAs(fund.NavBuckets));
            Assert.That(aligned.Metrics, Is.Not.Null);
            Assert.That(aligned.RuleFired, Is.EqualTo(fund.RuleFired));
            Assert.That(aligned.CriteriaEvaluation, Is.SameAs(fund.CriteriaEvaluation));
            Assert.That(aligned.CriteriaEvaluation!.MissingForUpgrade,
                Is.EqualTo(fund.CriteriaEvaluation!.MissingForUpgrade));
        });
    }

    #endregion

    #region Disk happy path

    [Test]
    public async Task RunAsync_RealHappyPathFixture_ProducesAlignmentEnrichedJson()
    {
        // Arrange — cascade fixtures up to step 04, fabricate step 03's output
        // (LLM-only, so no deterministic upstream), run step 05 against disk.
        const string runId = "test-happypath";
        await EnsureMacroFixtureExistsAsync(runId);

        var sut = new MacroAlignerAgent(_llmMock.Object);

        // Act
        var result = await sut.RunAsync("2026-W18", runId);

        // Assert
        var outPath = Paths.MacroAlignerOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<DataLoaderOutput>(
            await File.ReadAllTextAsync(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Is.Not.Empty);
            Assert.That(roundTripped!.Funds.All(f => f.MacroAlignment is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => f.MatchedTheme is not null), Is.True);
            // Step 04 fields preserved.
            Assert.That(roundTripped.Funds.All(f => f.Signal is not null), Is.True);
        });

        var json = await File.ReadAllTextAsync(outPath);
        Assert.That(json, Does.Contain("\"macro_alignment\":"));
        Assert.That(json, Does.Contain("\"matched_theme\":"));
    }

    private static async Task EnsureMacroFixtureExistsAsync(string runId)
    {
        var step3Path = Paths.MacroAnalystOutput("2026-W18", runId);
        var step4Path = Paths.SignalScorerOutput("2026-W18", runId);

        if (!File.Exists(step4Path))
        {
            var step2Path = Paths.MetricsCalculatorOutput("2026-W18", runId);
            if (!File.Exists(step2Path))
            {
                var step1Path = Paths.DataLoaderOutput("2026-W18", runId);
                if (!File.Exists(step1Path))
                {
                    new FikaFinans.InfrastructureV2.Tests.Agents.DataLoader.DataLoaderAgent()
                        .Run("schroder", "2026-W18", runId);
                }
                new FikaFinans.InfrastructureV2.Tests.Agents.MetricsCalculator.MetricsCalculatorAgent()
                    .Run("2026-W18", runId);
            }
            new FikaFinans.InfrastructureV2.Tests.Agents.SignalScorer.SignalScorerAgent()
                .Run("2026-W18", runId);
        }

        if (!File.Exists(step3Path))
        {
            var synthetic = MakeSyntheticMacroContext("2026-W18");
            Directory.CreateDirectory(Path.GetDirectoryName(step3Path)!);
            await File.WriteAllTextAsync(step3Path,
                System.Text.Json.JsonSerializer.Serialize(synthetic, JsonOptions.Default));
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
        Catalysts        = Array.Empty<Catalyst>(),
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

    private static RotationTheme MakeTheme(
        string id, string label, SignalStrength strength, IReadOnlyList<string> affected) => new()
    {
        Id                 = id,
        Label              = label,
        SignalStrength     = strength,
        AffectedCategories = affected,
        Rationale          = "synthetic",
        SourceChain        = null,
    };

    private static MacroContext MakeMacro(IReadOnlyList<RotationTheme> themes) => new()
    {
        GeneratedAt      = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek          = "2026-W18",
        ConfigVersion    = "1.0.0",
        SourceRunIds     = new SourceRunIds
        {
            WeeklySummaryRunId      = "ws-1",
            SubstitutionChainRunId  = "sc-1",
            RotationTargetsRunId    = "rt-1",
        },
        MacroRegime      = MacroRegime.Mixed,
        RegimeConfidence = 0.75m,
        NetMoodInput     = MarketSentiment.Mixed,
        Catalysts        = Array.Empty<Catalyst>(),
        RotationThemes   = themes,
        Warnings         = null,
    };

    private static FundRecord MakeFund(
        string isin,
        string category,
        SignalLabel signal,
        string? missingForUpgrade = null) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin, category),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = MakeMetrics(),
        Signal         = signal,
        RuleFired      = "synthetic_rule",
        CriteriaEvaluation = new CriteriaEvaluation
        {
            DataQualityWarnings = Array.Empty<string>(),
            MissingForUpgrade   = missingForUpgrade,
        },
    };

    private static FundMetadata MakeMetadata(string isin, string category) => new()
    {
        Isin                     = isin,
        Name                     = $"Test Fund {isin}",
        CompanyName              = "TestCo",
        CurrencyCode             = "SEK",
        Category                 = category,
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
        WindowsPositiveCount = 2,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = -2m,
        AnnVolatility2wPct   = 12m,
        Sharpe2w             = 0.5m,
        Sharpe12w            = 0.7m,
        Sharpe1y             = 0.6m,
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
