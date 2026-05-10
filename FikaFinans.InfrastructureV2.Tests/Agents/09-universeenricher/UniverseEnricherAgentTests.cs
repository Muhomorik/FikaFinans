using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Portfolio;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.UniverseEnricher;

[TestFixture]
[TestOf(typeof(UniverseEnricherAgent))]
public sealed class UniverseEnricherAgentTests
{
    private IFixture _fixture = null!;
    private Mock<IDifferentiatorLlmClient> _llmMock = null!;
    private UniverseEnricherAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _llmMock = _fixture.Freeze<Mock<IDifferentiatorLlmClient>>();
        _llmMock
            .Setup(x => x.WriteDifferentiatorsAsync(
                It.IsAny<DifferentiatorRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DifferentiatorRequest req, CancellationToken _) =>
                req.Alternatives.Select(a => new DifferentiatorLine
                {
                    Isin           = a.Isin,
                    Differentiator = $"Stub differentiator for {a.Metadata.Name}.",
                }).ToArray());
        _sut = _fixture.Create<UniverseEnricherAgent>();
    }

    #region Conviction scoring

    [Test]
    public async Task RunInMemoryAsync_StrengthCatalystEntry_HighConviction()
    {
        // Arrange — clean Buy: Strength + Valid + Strong macro + alternatives
        // present (one peer).
        var primary = MakeFund("LU0001",
            recommendation: Recommendation.CatalystEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Valid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongBuyMetrics());
        var peer = MakeFund("LU0002",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        WeakBuyMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(primary, peer));

        // Assert
        var enriched = result.Funds.First(f => f.Isin == "LU0001");
        Assert.Multiple(() =>
        {
            Assert.That(enriched.ConvictionScore, Is.GreaterThanOrEqualTo(0.85m));
            Assert.That(enriched.ConvictionBreakdown!.SignalStrength, Is.EqualTo(1.0m));
            Assert.That(enriched.ConvictionBreakdown.MacroAlignment, Is.EqualTo(1.0m));
            Assert.That(enriched.ConvictionBreakdown.ThesisValidity, Is.EqualTo(1.0m));
            Assert.That(enriched.Alternatives, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_NeutralSkip_LowConvictionContextDirectional()
    {
        // Arrange — Neutral funds get conviction 0 from signal and 0.5 from
        // thesis (non-directional). Result is in the low band.
        var fund = MakeFund("LU0010",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.ConvictionScore, Is.LessThan(0.30m));
            Assert.That(enriched.ConvictionBreakdown!.SignalStrength, Is.EqualTo(0.0m));
            Assert.That(enriched.ConvictionBreakdown.MacroAlignment, Is.EqualTo(0.0m));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_WeaknessThesisExitWithRotationPair_HighConviction()
    {
        // Arrange — paired exit with a CatalystEntry sharing matched_theme.id
        // gives universe_context = 1.0 and thesis_validity = 1.0 (Invalid for
        // Sells).
        var sell = MakeFund("LU0100",
            recommendation: Recommendation.ThesisExit,
            signal:         SignalLabel.Weakness,
            thesis:         ThesisValidity.Invalid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongSellMetrics(),
            matchedThemeId: "rot_theme_energy_2026-W18");
        var buy = MakeFund("LU0101",
            recommendation: Recommendation.CatalystEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Valid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongBuyMetrics(),
            matchedThemeId: "rot_theme_energy_2026-W18");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(sell, buy));

        // Assert
        var sellEnriched = result.Funds.First(f => f.Isin == "LU0100");
        var buyEnriched = result.Funds.First(f => f.Isin == "LU0101");
        Assert.Multiple(() =>
        {
            Assert.That(sellEnriched.RotationPairId, Is.EqualTo("rot_2026-W18_a"));
            Assert.That(buyEnriched.RotationPairId, Is.EqualTo("rot_2026-W18_a"));
            Assert.That(sellEnriched.ConvictionScore, Is.GreaterThanOrEqualTo(0.85m));
            Assert.That(sellEnriched.ConvictionBreakdown!.UniverseContext, Is.EqualTo(1.0m));
            Assert.That(sellEnriched.ConvictionBreakdown.ThesisValidity, Is.EqualTo(1.0m));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_BreakdownSumMatchesScore()
    {
        // Arrange — math integrity: weighted sum of components must equal
        // the rounded conviction_score within 0.01.
        var fund = MakeFund("LU0200",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        var b = enriched.ConvictionBreakdown!;
        var sum =
            0.25m * b.SignalStrength +
            0.25m * b.MetricsQuality +
            0.15m * b.MacroAlignment +
            0.20m * b.ThesisValidity +
            0.15m * b.UniverseContext;
        var expected = Math.Round(sum, 2, MidpointRounding.AwayFromZero);
        Assert.That(enriched.ConvictionScore, Is.EqualTo(expected));
    }

    #endregion

    #region Metrics quality penalties

    [Test]
    public async Task RunInMemoryAsync_DrawdownBeyondThreshold_PenalizesMetricsQuality()
    {
        // Arrange — sharpe 5/5 = 1.0 minus 0.20 drawdown penalty = 0.80.
        var fund = MakeFund("LU0301",
            recommendation: Recommendation.MomentumExit,
            signal:         SignalLabel.Weakness,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        new Metrics
            {
                WindowsPositiveCount = 1,
                WindowsTotal         = 3,
                CurrentDrawdownPct   = -5.0m,
                Sharpe2w             = -0.5m,
                Sharpe12w            = 5.0m,
                AnnVolatility12wPct  = 12m,
                DataQuality          = new MetricsDataQuality(),
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.That(enriched.ConvictionBreakdown!.MetricsQuality, Is.EqualTo(0.80m));
    }

    [Test]
    public async Task RunInMemoryAsync_HighVolatility_PenalizesMetricsQuality()
    {
        // Arrange — sharpe_12w 2.5/5 = 0.50, vol 30% > 25% → 0.50 - 0.15 = 0.35.
        var fund = MakeFund("LU0302",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        new Metrics
            {
                WindowsPositiveCount = 3,
                WindowsTotal         = 3,
                CurrentDrawdownPct   = 0m,
                Sharpe2w             = 1.5m,
                Sharpe12w            = 2.5m,
                AnnVolatility12wPct  = 30m,
                DataQuality          = new MetricsDataQuality(),
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ConvictionBreakdown!.MetricsQuality, Is.EqualTo(0.35m));
    }

    [Test]
    public async Task RunInMemoryAsync_NaNFlags_PenalizeMetricsQuality()
    {
        // Arrange — start at 1.0 (sharpe 5), subtract 0.10 per NaN flag (3) → 0.70.
        var fund = MakeFund("LU0303",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        new Metrics
            {
                WindowsPositiveCount = 3,
                WindowsTotal         = 3,
                CurrentDrawdownPct   = 0m,
                Sharpe2w             = 1.5m,
                Sharpe12w            = 5.0m,
                AnnVolatility12wPct  = 12m,
                DataQuality          = new MetricsDataQuality
                {
                    Sharpe2wIsNan  = true,
                    Sharpe12wIsNan = true,
                    Sharpe1yIsNan  = true,
                },
            });

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ConvictionBreakdown!.MetricsQuality, Is.EqualTo(0.70m));
    }

    [Test]
    public async Task RunInMemoryAsync_NullMetrics_MetricsQualityZero()
    {
        // Arrange
        var fund = MakeFund("LU0304",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        null);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        Assert.That(result.Funds.Single().ConvictionBreakdown!.MetricsQuality, Is.EqualTo(0.0m));
    }

    #endregion

    #region Universe rank

    [Test]
    public async Task RunInMemoryAsync_RanksByConvictionDescending_WithinRecommendationType()
    {
        // Arrange — three MomentumEntry funds with different conviction.
        var top = MakeFund("LU0400",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Valid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongBuyMetrics());
        var mid = MakeFund("LU0401",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics());
        var low = MakeFund("LU0402",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Invalid,
            macro:          MacroAlignment.None,
            metrics:        WeakBuyMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(top, mid, low));

        // Assert — three buys all in same category so each has 2 alternatives.
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.First(f => f.Isin == "LU0400").UniverseRank!.WithinRecommendation, Is.EqualTo(1));
            Assert.That(result.Funds.First(f => f.Isin == "LU0401").UniverseRank!.WithinRecommendation, Is.EqualTo(2));
            Assert.That(result.Funds.First(f => f.Isin == "LU0402").UniverseRank!.WithinRecommendation, Is.EqualTo(3));
            Assert.That(result.Funds.First(f => f.Isin == "LU0400").UniverseRank!.OfTotalInRecommendation, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_TieOnConviction_BreaksByLowerFee()
    {
        // Arrange — two funds, identical conviction inputs but different fees.
        var cheap = MakeFund("LU0500",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Valid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongBuyMetrics(),
            totalFee:       0.50m);
        var expensive = MakeFund("LU0501",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Valid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongBuyMetrics(),
            totalFee:       1.50m);

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(cheap, expensive));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.First(f => f.Isin == "LU0500").UniverseRank!.WithinRecommendation, Is.EqualTo(1));
            Assert.That(result.Funds.First(f => f.Isin == "LU0501").UniverseRank!.WithinRecommendation, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_SkipAndMaintain_NoUniverseRank()
    {
        // Arrange — only Skip / Maintain funds; no rankable category.
        var skip = MakeFund("LU0600",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics());
        var maintain = MakeFund("LU0601",
            recommendation: Recommendation.Maintain,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(skip, maintain));

        // Assert
        Assert.That(result.Funds.All(f => f.UniverseRank is null), Is.True);
    }

    #endregion

    #region Alternatives

    [Test]
    public async Task RunInMemoryAsync_BuyCandidate_GetsCategoryPeersAsAlternatives()
    {
        // Arrange — primary + 2 peers in same category, 1 peer in different
        // category should be filtered out.
        var primary = MakeFund("LU0700",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics(),
            category:       "Globalfond");
        var peerA = MakeFund("LU0701",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics(),
            category:       "Globalfond");
        var peerB = MakeFund("LU0702",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics(),
            category:       "Globalfond");
        var unrelated = MakeFund("LU0703",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics(),
            category:       "Branschfond, Energi");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(primary, peerA, peerB, unrelated));

        // Assert
        var enriched = result.Funds.First(f => f.Isin == "LU0700");
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Alternatives, Has.Count.EqualTo(2));
            Assert.That(enriched.Alternatives!.Select(a => a.Isin.Value),
                Is.EquivalentTo(new[] { "LU0701", "LU0702" }));
            Assert.That(enriched.Alternatives!.All(a => a.Differentiator.StartsWith("Stub")), Is.True);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_BuyCandidate_HonorsMaxPerFund()
    {
        // Arrange — config max_per_fund = 3; provide 5 peers, expect 3.
        var primary = MakeFund("LU0800",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics());
        var peers = Enumerable.Range(1, 5).Select(i => MakeFund(
            $"LU080{i}",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics())).ToArray();

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(new[] { primary }.Concat(peers).ToArray()));

        // Assert
        Assert.That(result.Funds.First(f => f.Isin == "LU0800").Alternatives, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task RunInMemoryAsync_LlmThrows_AlternativesPopulatedWithEmptyDifferentiators()
    {
        // Arrange — LLM faults; structural alternatives still emit.
        _llmMock
            .Setup(x => x.WriteDifferentiatorsAsync(
                It.IsAny<DifferentiatorRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated LLM 503"));

        var primary = MakeFund("LU0900",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics());
        var peer = MakeFund("LU0901",
            recommendation: Recommendation.Skip,
            signal:         SignalLabel.Neutral,
            thesis:         ThesisValidity.NotApplicable,
            macro:          MacroAlignment.None,
            metrics:        NeutralMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(primary, peer));

        // Assert
        var enriched = result.Funds.First(f => f.Isin == "LU0900");
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Alternatives, Has.Count.EqualTo(1));
            Assert.That(enriched.Alternatives![0].Differentiator, Is.Empty);
            Assert.That(result.DataQuality.Warnings, Has.Some.Contains("LU0900"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_SellCandidate_NoAlternatives()
    {
        // Arrange
        var sell = MakeFund("LU1000",
            recommendation: Recommendation.ThesisExit,
            signal:         SignalLabel.Weakness,
            thesis:         ThesisValidity.Invalid,
            macro:          MacroAlignment.Strong,
            metrics:        StrongSellMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(sell));

        // Assert
        Assert.That(result.Funds.Single().Alternatives, Is.Null);
    }

    #endregion

    #region Rotation pairing

    [Test]
    public async Task RunInMemoryAsync_NoExitInTheme_NoRotationPair()
    {
        // Arrange — only Buys in this theme, no pairing.
        var buy = MakeFund("LU1100",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics(),
            matchedThemeId: "rot_theme_x");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(buy));

        // Assert
        Assert.That(result.Funds.Single().RotationPairId, Is.Null);
    }

    [Test]
    public async Task RunInMemoryAsync_TwoThemes_LettersAssignedByPairCountThenAlphabet()
    {
        // Arrange — theme "energy" has 3 paired funds (1 sell + 2 buys),
        // theme "asia" has 2 paired funds (1 sell + 1 buy). Energy gets 'a'.
        var energySell = MakeFund("LU1200",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis:         ThesisValidity.Invalid, macro: MacroAlignment.Strong,
            metrics:        StrongSellMetrics(), matchedThemeId: "rot_theme_energy");
        var energyBuy1 = MakeFund("LU1201",
            recommendation: Recommendation.CatalystEntry, signal: SignalLabel.Strength,
            thesis:         ThesisValidity.Valid, macro: MacroAlignment.Strong,
            metrics:        StrongBuyMetrics(), matchedThemeId: "rot_theme_energy");
        var energyBuy2 = MakeFund("LU1202",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis:         ThesisValidity.Partial, macro: MacroAlignment.Partial,
            metrics:        StrongBuyMetrics(), matchedThemeId: "rot_theme_energy");
        var asiaSell = MakeFund("LU1203",
            recommendation: Recommendation.MomentumExit, signal: SignalLabel.Weakness,
            thesis:         ThesisValidity.Partial, macro: MacroAlignment.Partial,
            metrics:        StrongSellMetrics(), matchedThemeId: "rot_theme_asia");
        var asiaBuy = MakeFund("LU1204",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis:         ThesisValidity.Partial, macro: MacroAlignment.Partial,
            metrics:        StrongBuyMetrics(), matchedThemeId: "rot_theme_asia");

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(
            energySell, energyBuy1, energyBuy2, asiaSell, asiaBuy));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.First(f => f.Isin == "LU1200").RotationPairId,
                Is.EqualTo("rot_2026-W18_a"));
            Assert.That(result.Funds.First(f => f.Isin == "LU1201").RotationPairId,
                Is.EqualTo("rot_2026-W18_a"));
            Assert.That(result.Funds.First(f => f.Isin == "LU1202").RotationPairId,
                Is.EqualTo("rot_2026-W18_a"));
            Assert.That(result.Funds.First(f => f.Isin == "LU1203").RotationPairId,
                Is.EqualTo("rot_2026-W18_b"));
            Assert.That(result.Funds.First(f => f.Isin == "LU1204").RotationPairId,
                Is.EqualTo("rot_2026-W18_b"));
        });
    }

    #endregion

    #region Failure modes / append-only

    [Test]
    public void Constructor_WeightsDontSumToOne_HaltsAtRun()
    {
        // Arrange
        var badConfig = new UniverseEnricherConfig
        {
            Weights = new ConvictionWeights
            {
                SignalStrength  = 0.30m,
                MetricsQuality  = 0.30m,
                MacroAlignment  = 0.30m,
                ThesisValidity  = 0.30m,
                UniverseContext = 0.30m,
            },
        };
        var llm = new Mock<IDifferentiatorLlmClient>().Object;
        var sut = new UniverseEnricherAgent(new TestPathsService(), llm, badConfig);

        // Act + Assert — fail fast at first call.
        Assert.That(async () => await sut.RunInMemoryAsync(MakeOutput()),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task RunInMemoryAsync_PreservesUpstreamFields()
    {
        // Arrange
        var fund = MakeFund("LU1300",
            recommendation: Recommendation.MomentumEntry,
            signal:         SignalLabel.Strength,
            thesis:         ThesisValidity.Partial,
            macro:          MacroAlignment.Partial,
            metrics:        StrongBuyMetrics());

        // Act
        var result = await _sut.RunInMemoryAsync(MakeOutput(fund));

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(enriched.Metrics, Is.SameAs(fund.Metrics));
            Assert.That(enriched.Signal, Is.EqualTo(fund.Signal));
            Assert.That(enriched.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(enriched.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(enriched.ThesisValidity, Is.EqualTo(fund.ThesisValidity));
            Assert.That(enriched.Recommendation, Is.EqualTo(fund.Recommendation));
            Assert.That(enriched.RecommendationReason, Is.EqualTo(fund.RecommendationReason));
        });
    }

    #endregion

    #region Disk happy path

    [Test]
    public async Task Run_RealHappyPathFixture_ProducesEnrichmentJson()
    {
        // Arrange — cascade fixtures up to step 08, run step 09.
        const string runId = "test-happypath";
        await EnsureRecommenderFixtureExistsAsync(runId);

        var sut = _fixture.Create<UniverseEnricherAgent>();

        // Act
        var result = await sut.RunAsync("2026-W18", runId);

        // Assert
        var outPath = Paths.UniverseEnricherOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Is.Not.Empty);
            Assert.That(roundTripped!.Funds.All(f => f.ConvictionScore is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => f.ConvictionBreakdown is not null), Is.True);
            // Step 08 fields preserved.
            Assert.That(roundTripped.Funds.All(f => f.Recommendation is not null), Is.True);
            Assert.That(roundTripped.Funds.All(f => !string.IsNullOrEmpty(f.RecommendationReason)), Is.True);
        });

        var json = await File.ReadAllTextAsync(outPath);
        Assert.That(json, Does.Contain("\"conviction_score\":"));
        Assert.That(json, Does.Contain("\"conviction_breakdown\":"));
    }

    private static async Task EnsureRecommenderFixtureExistsAsync(string runId)
    {
        var step8Path = Paths.RecommenderOutput("2026-W18", runId);
        if (File.Exists(step8Path)) return;

        var step7Path = Paths.ThesisValidatorOutput("2026-W18", runId);
        var step6Path = Paths.CatalystTaggerOutput("2026-W18", runId);
        var step5Path = Paths.MacroAlignerOutput("2026-W18", runId);
        var step3Path = Paths.MacroAnalystOutput("2026-W18", runId);
        var step4Path = Paths.SignalScorerOutput("2026-W18", runId);
        var step2Path = Paths.MetricsCalculatorOutput("2026-W18", runId);
        var step1Path = Paths.DataLoaderOutput("2026-W18", runId);

        if (!File.Exists(step1Path))
        {
            new FikaFinans.Infrastructure.Pipeline.Agents.DataLoaderAgent(new TestPathsService())
                .Run("schroder", "2026-W18", runId);
        }
        if (!File.Exists(step2Path))
        {
            new FikaFinans.Infrastructure.Pipeline.Agents.MetricsCalculatorAgent(new TestPathsService())
                .Run("2026-W18", runId);
        }
        if (!File.Exists(step4Path))
        {
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

        new RecommenderAgent(new TestPathsService()).Run("2026-W18", runId);
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
        Recommendation recommendation,
        SignalLabel signal,
        ThesisValidity thesis,
        MacroAlignment macro,
        Metrics? metrics,
        string? matchedThemeId = null,
        decimal totalFee = 1.0m,
        string category = "Globalfond") => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin, totalFee, category),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = metrics,
        Signal         = signal,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = macro,
        MatchedTheme      = matchedThemeId is null ? null : new MatchedTheme
        {
            Id          = matchedThemeId,
            Label       = "Theme",
            MatchMethod = MatchMethod.DirectCategory,
        },
        PromotedToForming = false,
        PromotionReason   = null,
        Catalyst          = null,
        ThesisValidity    = thesis,
        ThesisRationale   = "Upstream rationale.",
        ThesisMethod      = ThesisMethod.Matrix,
        Recommendation    = recommendation,
        RecommendationReason = $"synthetic for {recommendation}",
    };

    private static FundMetadata MakeMetadata(string isin, decimal totalFee, string category) => new()
    {
        Isin                     = isin,
        Name                     = $"Test Fund {isin}",
        CompanyName              = "TestCo",
        CurrencyCode             = "SEK",
        Category                 = category,
        FundType                 = "EQUITY_FUND",
        IsIndexFund              = false,
        ManagedType              = "ACTIVE",
        TotalFee                 = totalFee,
        ManagementFee            = 0.7m,
        Risk                     = 4,
        Rating                   = 3,
        SharpeRatioStatic        = 1.0m,
        StandardDeviationStatic  = 12.0m,
        RecommendedHoldingPeriod = "FIVE_YEAR",
        Capital                  = 1_000_000m,
        NumberOfOwners           = 100,
    };

    private static Metrics StrongBuyMetrics() => new()
    {
        WindowsPositiveCount = 3,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = 0m,
        Sharpe2w             = 2.0m,
        Sharpe12w            = 5.0m,
        Sharpe1y             = 1.5m,
        AnnVolatility12wPct  = 14m,
        AnnVolatility1yPct   = 16m,
        Return12wCompoundPct = 8m,
        Return1yCompoundPct  = 18m,
        MaxDrawdown12wPct    = -2m,
        MaxDrawdown1yPct     = -6m,
        DataQuality          = new MetricsDataQuality(),
    };

    private static Metrics WeakBuyMetrics() => new()
    {
        WindowsPositiveCount = 3,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = 0m,
        Sharpe2w             = 0.5m,
        Sharpe12w            = 0.6m,
        Sharpe1y             = 0.4m,
        AnnVolatility12wPct  = 12m,
        AnnVolatility1yPct   = 13m,
        Return12wCompoundPct = 3m,
        Return1yCompoundPct  = 5m,
        MaxDrawdown12wPct    = -2m,
        MaxDrawdown1yPct     = -5m,
        DataQuality          = new MetricsDataQuality(),
    };

    private static Metrics StrongSellMetrics() => new()
    {
        WindowsPositiveCount = 0,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = -5.5m,
        Sharpe2w             = -1.2m,
        Sharpe12w            = 3.5m,
        Sharpe1y             = 1.0m,
        AnnVolatility12wPct  = 18m,
        AnnVolatility1yPct   = 20m,
        Return12wCompoundPct = 4m,
        Return1yCompoundPct  = 10m,
        MaxDrawdown12wPct    = -7m,
        MaxDrawdown1yPct     = -10m,
        DataQuality          = new MetricsDataQuality(),
    };

    private static Metrics NeutralMetrics() => new()
    {
        WindowsPositiveCount = 1,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = -0.5m,
        Sharpe2w             = 0.1m,
        Sharpe12w            = 0.3m,
        Sharpe1y             = 0.4m,
        AnnVolatility12wPct  = 12m,
        AnnVolatility1yPct   = 14m,
        Return12wCompoundPct = 1m,
        Return1yCompoundPct  = 4m,
        MaxDrawdown12wPct    = -3m,
        MaxDrawdown1yPct     = -6m,
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
