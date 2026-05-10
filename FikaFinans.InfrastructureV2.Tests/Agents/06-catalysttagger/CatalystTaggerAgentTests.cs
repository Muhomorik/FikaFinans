using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Application.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

[TestFixture]
[TestOf(typeof(CatalystTaggerAgent))]
public sealed class CatalystTaggerAgentTests
{
    private IFixture _fixture = null!;
    private Mock<IFundCatalystLlmClient> _llmMock = null!;
    private CatalystTaggerAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _llmMock = _fixture.Freeze<Mock<IFundCatalystLlmClient>>();
        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CatalystExposureClassification>());
        _sut = _fixture.Create<CatalystTaggerAgent>();
    }

    #region Direct / Indirect / None

    [Test]
    public async Task RunInMemoryAsync_DirectMatch_PopulatesCatalystWithExposureDirect()
    {
        // Arrange
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi", "Energy"]);
        var fund = MakeFund("LU0001", category: "Branschfond, Energi", name: "Global Energy");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Hormuz disruption", ExposureKind.Direct,
                    "Energy sector fund directly benefits from oil price spikes."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        var tagged = result.Funds.Single();
        Assert.That(tagged.Catalyst, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(tagged.Catalyst!.Name, Is.EqualTo("Hormuz disruption"));
            Assert.That(tagged.Catalyst.Intensity, Is.EqualTo(Intensity.High));
            Assert.That(tagged.Catalyst.WeeksActive, Is.EqualTo(8));
            Assert.That(tagged.Catalyst.ExposureType, Is.EqualTo(ExposureType.Direct));
            Assert.That(tagged.Catalyst.Rationale, Does.Contain("oil price"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_IndirectMatch_ExposureTypeIndirect()
    {
        // Arrange
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0002", category: "Branschfond, Ny energi", name: "Glbl Alt Engy");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Hormuz disruption", ExposureKind.Indirect,
                    "Alt energy benefits as investors hedge fossil fuel volatility."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        Assert.That(result.Funds.Single().Catalyst!.ExposureType,
            Is.EqualTo(ExposureType.Indirect));
    }

    [Test]
    public async Task RunInMemoryAsync_NoMatch_CatalystNull()
    {
        // Arrange — LLM returns None for the only catalyst.
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0003", category: "Tillväxtmarknader", name: "Em Mkts");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Hormuz disruption", ExposureKind.None, "Em Mkts not affected."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        Assert.That(result.Funds.Single().Catalyst, Is.Null);
    }

    [Test]
    public async Task RunInMemoryAsync_LlmReturnsEmptyList_CatalystNull()
    {
        // Arrange
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0004", category: "Räntefond", name: "Bond fund");

        // Act — default mock returns empty list.
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        Assert.That(result.Funds.Single().Catalyst, Is.Null);
    }

    #endregion

    #region Selection / tie-breaks

    [Test]
    public async Task RunInMemoryAsync_TwoCompetingCatalysts_PicksDirectOverIndirect()
    {
        // Arrange
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 10,
            ["Branschfond, Energi"]);
        var ai = MakeCatalyst("AI capex cycle", Intensity.Medium, weeksActive: 4,
            ["Branschfond, Teknik"]);
        var fund = MakeFund("LU0005", category: "Branschfond, Teknik", name: "Tech fund");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Hormuz disruption", ExposureKind.Indirect, "Cousin sector."),
                Classify("AI capex cycle",   ExposureKind.Direct,   "Tech direct beneficiary."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(
            MakeOutput(fund), MakeMacro([hormuz, ai]));

        // Assert — Direct beats Indirect even though Hormuz has higher intensity.
        var tagged = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(tagged.Catalyst!.Name, Is.EqualTo("AI capex cycle"));
            Assert.That(tagged.Catalyst.ExposureType, Is.EqualTo(ExposureType.Direct));
            Assert.That(tagged.Catalyst.Intensity, Is.EqualTo(Intensity.Medium));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_TwoDirectMatches_PicksHigherIntensity()
    {
        // Arrange — both Direct; high intensity should beat medium.
        var high = MakeCatalyst("Big shock", Intensity.High, weeksActive: 4,
            ["Branschfond, Teknik"]);
        var med = MakeCatalyst("Small shock", Intensity.Medium, weeksActive: 4,
            ["Branschfond, Teknik"]);
        var fund = MakeFund("LU0006", category: "Branschfond, Teknik", name: "Tech");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Big shock",   ExposureKind.Direct, "Direct."),
                Classify("Small shock", ExposureKind.Direct, "Also direct."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(
            MakeOutput(fund), MakeMacro([high, med]));

        // Assert
        Assert.That(result.Funds.Single().Catalyst!.Name, Is.EqualTo("Big shock"));
    }

    [Test]
    public async Task RunInMemoryAsync_TwoDirectSameIntensity_PicksLongerWeeksActive()
    {
        // Arrange
        var older = MakeCatalyst("Older", Intensity.High, weeksActive: 12,
            ["Branschfond, Teknik"]);
        var newer = MakeCatalyst("Newer", Intensity.High, weeksActive: 2,
            ["Branschfond, Teknik"]);
        var fund = MakeFund("LU0007", category: "Branschfond, Teknik", name: "Tech");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Older", ExposureKind.Direct, "Direct."),
                Classify("Newer", ExposureKind.Direct, "Direct."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(
            MakeOutput(fund), MakeMacro([older, newer]));

        // Assert
        Assert.That(result.Funds.Single().Catalyst!.Name, Is.EqualTo("Older"));
    }

    [Test]
    public async Task RunInMemoryAsync_LlmHallucinatesCatalystNotInActiveList_DroppedSilently()
    {
        // Arrange — LLM mentions "Banking crisis" which isn't in the active list.
        // The honest "Hormuz Direct" should win.
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0008", category: "Branschfond, Energi", name: "Energy");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Classify("Banking crisis",   ExposureKind.Direct, "Hallucinated."),
                Classify("Hormuz disruption", ExposureKind.Direct, "Real catalyst."),
            ]);

        // Act
        var result = await _sut.RunInMemoryAsync(
            MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        Assert.That(result.Funds.Single().Catalyst!.Name, Is.EqualTo("Hormuz disruption"));
    }

    #endregion

    #region Failure modes

    [Test]
    public async Task RunInMemoryAsync_EmptyCatalysts_NoLlmCallAllNull()
    {
        // Arrange
        var f1 = MakeFund("LU0009", category: "Branschfond, Energi", name: "Energy");
        var f2 = MakeFund("LU0010", category: "Tillväxtmarknader", name: "EM");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(f1, f2), MakeMacro([]));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.All(f => f.Catalyst is null), Is.True);
        });
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Catalyst>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_FundCategoryEmpty_CatalystNullAndWarn()
    {
        // Arrange
        var hormuz = MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0011", category: "", name: "Bad fund");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Single().Catalyst, Is.Null);
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("LU0011"));
        });
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Catalyst>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_CatalystWithEmptyAffectedCategories_SkippedDefensively()
    {
        // Arrange — the empty-affected catalyst should be filtered before LLM call.
        var empty = MakeCatalyst("Phantom", Intensity.High, weeksActive: 1,
            Array.Empty<string>());
        var real = MakeCatalyst("Hormuz", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0012", category: "Branschfond, Energi", name: "Energy");

        IReadOnlyList<Catalyst>? captured = null;
        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<Catalyst>, CancellationToken>(
                (_, _, list, _) => captured = list)
            .ReturnsAsync([Classify("Hormuz", ExposureKind.Direct, "Direct.")]);

        // Act
        await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([empty, real]));

        // Assert
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Select(c => c.Name), Is.EquivalentTo(new[] { "Hormuz" }));
    }

    #endregion

    #region Append-only

    [Test]
    public async Task RunInMemoryAsync_PreservesUpstreamFields()
    {
        // Arrange
        var hormuz = MakeCatalyst("Hormuz", Intensity.High, weeksActive: 8,
            ["Branschfond, Energi"]);
        var fund = MakeFund("LU0013", category: "Branschfond, Energi", name: "Energy");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Classify("Hormuz", ExposureKind.Direct, "Direct.")]);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund), MakeMacro([hormuz]));

        // Assert
        var tagged = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(tagged.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(tagged.NavBuckets, Is.SameAs(fund.NavBuckets));
            Assert.That(tagged.Metrics, Is.Not.Null);
            Assert.That(tagged.Signal, Is.EqualTo(fund.Signal));
            Assert.That(tagged.RuleFired, Is.EqualTo(fund.RuleFired));
            Assert.That(tagged.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(tagged.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(tagged.PromotedToForming, Is.EqualTo(fund.PromotedToForming));
        });
    }

    #endregion

    #region Disk happy path

    [Test]
    public async Task RunAsync_RealHappyPathFixture_ProducesCatalystEnrichedJson()
    {
        // Arrange — cascade fixtures up to step 05, stub the LLM, run step 06.
        const string runId = "test-happypath";
        await EnsureMacroAlignerFixtureExistsAsync(runId);

        var sut = _fixture.Create<CatalystTaggerAgent>();

        // Act
        var result = await sut.RunAsync("2026-W18", runId);

        // Assert
        var outPath = Paths.CatalystTaggerOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Is.Not.Empty);
            // Step 05 fields preserved.
            Assert.That(roundTripped!.Funds.All(f => f.MatchedTheme is not null), Is.True);
            // Step 06 field present (catalyst may be null for any given fund — that's fine).
            // Just check the JSON contains the field.
        });

        var json = await File.ReadAllTextAsync(outPath);
        Assert.That(json, Does.Contain("\"catalyst\":"));
    }

    private async Task EnsureMacroAlignerFixtureExistsAsync(string runId)
    {
        // Cascade — run prior steps if their outputs are missing.
        var step5Path = Paths.MacroAlignerOutput("2026-W18", runId);
        var step3Path = Paths.MacroAnalystOutput("2026-W18", runId);

        if (File.Exists(step5Path) && File.Exists(step3Path)) return;

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

        // Step 03 (LLM-only): fabricate a synthetic MacroContext on disk so step 05/06 can read it.
        if (!File.Exists(step3Path))
        {
            var synthetic = MakeSyntheticMacroContext("2026-W18");
            Directory.CreateDirectory(Path.GetDirectoryName(step3Path)!);
            await File.WriteAllTextAsync(step3Path,
                JsonSerializer.Serialize(synthetic, JsonOptions.Default));
        }

        // Step 05: run with a stub LLM (defaults to None — direct matches still resolve
        // deterministically from the rotation themes we just wrote).
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
            MakeCatalyst("Hormuz disruption", Intensity.High, weeksActive: 8,
                ["Branschfond, Energi", "Råvarufond"]),
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

    private static Catalyst MakeCatalyst(
        string name, Intensity intensity, int weeksActive, IReadOnlyList<string> affected) => new()
    {
        Name               = name,
        Intensity          = intensity,
        WeeksActive        = weeksActive,
        AffectedCategories = affected,
        Rationale          = "synthetic",
    };

    private static CatalystExposureClassification Classify(
        string name, ExposureKind exposure, string rationale) => new()
    {
        CatalystName = name,
        Exposure     = exposure,
        Rationale    = rationale,
    };

    private static MacroContext MakeMacro(IReadOnlyList<Catalyst> catalysts) => new()
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
        Catalysts        = catalysts,
        RotationThemes   = Array.Empty<RotationTheme>(),
        Warnings         = null,
    };

    private static FundRecord MakeFund(string isin, string category, string name) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin, category, name),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = MakeMetrics(),
        Signal         = SignalLabel.Strength,
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
    };

    private static FundMetadata MakeMetadata(string isin, string category, string name) => new()
    {
        Isin                     = isin,
        Name                     = name,
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
