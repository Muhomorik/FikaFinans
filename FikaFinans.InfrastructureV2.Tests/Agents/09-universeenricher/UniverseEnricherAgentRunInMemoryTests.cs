using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.UniverseEnricher;

/// <summary>
/// The domain-input <c>RunInMemoryAsync</c> — the call the signal-driven step makes, steps
/// 4–8's twin. The four passes themselves are covered in <see cref="UniverseEnricherAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(UniverseEnricherAgent))]
public sealed class UniverseEnricherAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W43");
    private static readonly PipelineRunId RunId = new("run-9");

    private IFixture _fixture = null!;
    private Mock<IDifferentiatorLlmClient> _llm = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);

        _llm = _fixture.Freeze<Mock<IDifferentiatorLlmClient>>();
        _llm
            .Setup(x => x.WriteDifferentiatorsAsync(It.IsAny<DifferentiatorRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DifferentiatorRequest req, CancellationToken _) =>
                req.Alternatives.Select(a => new DifferentiatorLine
                {
                    Isin           = a.Isin,
                    Differentiator = $"Stub for {a.Metadata.Name}.",
                }).ToArray());
    }

    private UniverseEnricherAgent CreateSut() => _fixture.Create<UniverseEnricherAgent>();

    [Test]
    public async Task RunInMemoryAsync_ExitAndEntryInOneTheme_PairIdCarriesTheGivenWeek()
    {
        // Arrange — decided on step 9's page: the week the run happens in.
        var exit = MakeFund("LU9001", Recommendation.ThesisExit, held: true);
        var entry = MakeFund("LU9002", Recommendation.CatalystEntry);

        // Act
        var result = await CreateSut().RunInMemoryAsync(
            Family, Week, RunId, [exit, entry], UniverseEnricherConfig.Default);

        // Assert
        Assert.That(result.Funds.Select(f => f.RotationPairId),
            Is.EqualTo(new[] { "rot_2026-W43_a", "rot_2026-W43_a" }));
    }

    [Test]
    public async Task RunInMemoryAsync_ConfigWeights_ScoreWithThem()
    {
        // Arrange — everything on macro alignment: a Strong fund scores exactly 1.
        var config = new UniverseEnricherConfig
        {
            Weights = new ConvictionWeights
            {
                SignalStrength = 0m, MetricsQuality = 0m, MacroAlignment = 1.0m,
                ThesisValidity = 0m, UniverseContext = 0m,
            },
        };

        // Act
        var result = await CreateSut().RunInMemoryAsync(
            Family, Week, RunId, [MakeFund("LU9003", Recommendation.MomentumEntry)], config);

        // Assert
        Assert.That(result.Funds.Single().ConvictionScore, Is.EqualTo(1.0m));
    }

    [Test]
    public async Task RunInMemoryAsync_ConfigMaxPerFund_LimitsAlternatives()
    {
        // Arrange — three peers in the Buy's category, room for one.
        var config = new UniverseEnricherConfig { Alternatives = new AlternativesPolicy { MaxPerFund = 1 } };
        var funds = new[]
        {
            MakeFund("LU9004", Recommendation.CatalystEntry),
            MakeFund("LU9005", Recommendation.Skip),
            MakeFund("LU9006", Recommendation.Skip),
            MakeFund("LU9007", Recommendation.Skip),
        };

        // Act
        var result = await CreateSut().RunInMemoryAsync(Family, Week, RunId, funds, config);

        // Assert
        Assert.That(result.Funds.Single(f => f.Isin == "LU9004").Alternatives, Has.Count.EqualTo(1));
    }

    [Test]
    public void RunInMemoryAsync_ConfigWeightsNotSummingToOne_ThrowsInvalidDataException()
    {
        // Arrange — the weights check stays in the agent, so a bad edit fails the run.
        var config = new UniverseEnricherConfig { Weights = new ConvictionWeights { SignalStrength = 0.90m } };

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().RunInMemoryAsync(
            Family, Week, RunId, [MakeFund("LU9008", Recommendation.MomentumEntry)], config));
    }

    [Test]
    public async Task RunInMemoryAsync_TwoEntries_RanksThemAcrossTheGivenRecords()
    {
        // Arrange
        var strong = MakeFund("LU9009", Recommendation.MomentumEntry, macro: MacroAlignment.Strong);
        var weak = MakeFund("LU9010", Recommendation.MomentumEntry, macro: MacroAlignment.None);

        // Act
        var result = await CreateSut().RunInMemoryAsync(
            Family, Week, RunId, [weak, strong], UniverseEnricherConfig.Default);

        // Assert
        var ranks = result.Funds.ToDictionary(f => f.Isin.Value, f => f.UniverseRank!);
        Assert.Multiple(() =>
        {
            Assert.That(ranks["LU9009"].WithinRecommendation, Is.EqualTo(1));
            Assert.That(ranks["LU9010"].WithinRecommendation, Is.EqualTo(2));
            Assert.That(ranks["LU9010"].OfTotalInRecommendation, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_LlmFails_WarningLandsInDataQualityWarnings()
    {
        // Arrange
        _llm
            .Setup(x => x.WriteDifferentiatorsAsync(It.IsAny<DifferentiatorRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outage"));
        var funds = new[] { MakeFund("LU9011", Recommendation.CatalystEntry), MakeFund("LU9012", Recommendation.Skip) };

        // Act
        var result = await CreateSut().RunInMemoryAsync(Family, Week, RunId, funds, UniverseEnricherConfig.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.DataQuality.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.DataQuality.Warnings[0], Does.Contain("LU9011"));
            Assert.That(result.Funds.Single(f => f.Isin == "LU9011").Alternatives![0].Differentiator, Is.Empty);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_Always_StampsEnvelopeFromArguments()
    {
        // Act
        var result = await CreateSut().RunInMemoryAsync(
            Family, Week, RunId, Array.Empty<FundRecord>(), UniverseEnricherConfig.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Family, Is.EqualTo("schroder"));
            Assert.That(result.IsoWeek, Is.EqualTo("2026-W43"));
            Assert.That(result.RunId, Is.EqualTo(RunId));
            Assert.That(result.Funds, Is.Empty);
            Assert.That(result.DataQuality.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_Always_LeavesPortfolioFieldsEmpty()
    {
        // Arrange — no per-fund record carries them; an open question on step 9's page.

        // Act
        var result = await CreateSut().RunInMemoryAsync(
            Family, Week, RunId, [MakeFund("LU9013", Recommendation.Maintain, held: true)],
            UniverseEnricherConfig.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.FrozenPositions, Is.Empty);
            Assert.That(result.CashAvailableKr, Is.Zero);
        });
    }

    [Test]
    public void RunInMemoryAsync_NullFunds_Throws()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => CreateSut().RunInMemoryAsync(
            Family, Week, RunId, null!, UniverseEnricherConfig.Default));
    }

    [Test]
    public void RunInMemoryAsync_NullConfig_Throws()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => CreateSut().RunInMemoryAsync(
            Family, Week, RunId, Array.Empty<FundRecord>(), null!));
    }

    // ────────────────────────── helpers ──────────────────────────

    private static FundRecord MakeFund(
        string isin, Recommendation recommendation, bool held = false,
        MacroAlignment macro = MacroAlignment.Strong) => new()
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
        CurrentlyHeld = held,
        Layer = FundLayer.Active,
        Signal = recommendation is Recommendation.ThesisExit or Recommendation.MomentumExit
            ? SignalLabel.Weakness
            : SignalLabel.Strength,
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
        ThesisValidity = ThesisValidity.Valid,
        ThesisRationale = "Upstream rationale.",
        ThesisMethod = ThesisMethod.Matrix,
        Recommendation = recommendation,
        RecommendationReason = "synthetic",
    };
}
