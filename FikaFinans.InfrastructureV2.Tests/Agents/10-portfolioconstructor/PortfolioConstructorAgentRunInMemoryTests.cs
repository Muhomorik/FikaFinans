using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Json;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.PortfolioConstructor;

/// <summary>
/// The domain-input <c>RunInMemory</c> — the call the signal-driven step makes, steps 4–9's
/// twin. The passes themselves are covered in <see cref="PortfolioConstructorAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(PortfolioConstructorAgent))]
public sealed class PortfolioConstructorAgentRunInMemoryTests
{
    private static readonly IsoWeek Week = new("2026-W46");

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);
    }

    private PortfolioConstructorAgent CreateSut() => _fixture.Create<PortfolioConstructorAgent>();

    [Test]
    public void RunInMemory_Records_StampsTheWeekAndTouchesNoFile()
    {
        // Arrange
        var buy = MakeFund("LU0BUY", Recommendation.CatalystEntry, conviction: 0.78m);

        // Act
        var result = CreateSut().RunInMemory(Week, [buy], [], 50_000m, PortfolioConstructorConfig.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsoWeek, Is.EqualTo(Week.Value));
            Assert.That(result.ConfigVersion, Is.EqualTo(PortfolioConstructorConfig.ExpectedConfigVersion));
            Assert.That(result.Trades, Has.One.Matches<Trade>(t => t.TradeType == TradeType.Buy));
        });
    }

    [Test]
    public void RunInMemory_ConfigGiven_UsesItNotTheCtorDefaults()
    {
        // Arrange — 0.50 passes the default buy floor of 0.30 but not this config's 0.60.
        var buy = MakeFund("LU0BUY", Recommendation.MomentumEntry, conviction: 0.50m);
        var config = new PortfolioConstructorConfig
        {
            Meta   = new ConfigMeta { ConfigVersion = PortfolioConstructorConfig.ExpectedConfigVersion },
            Guards = new ConvictionGuards { SkipBuyBelowConviction = 0.60m },
        };

        // Act
        var result = CreateSut().RunInMemory(Week, [buy], [], 50_000m, config);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Count(t => t.TradeType == TradeType.Buy), Is.Zero);
            Assert.That(result.RejectedRecommendations, Has.Count.EqualTo(1));
            Assert.That(result.RejectedRecommendations[0].RejectedBecause, Does.Contain("conviction_below_floor"));
        });
    }

    [Test]
    public void RunInMemory_ConfigVersionDrift_ThrowsInvalidDataException()
    {
        // Arrange — the provider hands the config back unchecked; the run is where drift halts.
        var config = new PortfolioConstructorConfig { Meta = new ConfigMeta { ConfigVersion = "9.9.9" } };

        // Act & Assert
        Assert.That(() => CreateSut().RunInMemory(Week, [], [], 0m, config),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("9.9.9"));
    }

    [Test]
    public void RunInMemory_CashFrozenAndHeld_AllCountTowardThePortfolio()
    {
        // Arrange — 40k held + 10k frozen + 50k cash = 100k.
        var held = MakeFund("LU0HELD", Recommendation.Maintain, conviction: 0.50m,
            currentlyHeld: true, currentValueKr: 40_000m);
        var frozen = new FrozenPosition { Name = "Old writeoff", CurrentValueKr = 10_000m, Reason = "writeoff" };

        // Act
        var result = CreateSut().RunInMemory(Week, [held], [frozen], 50_000m, PortfolioConstructorConfig.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.CapitalSummary.ActivePositionsValueKr, Is.EqualTo(40_000m));
            Assert.That(result.CapitalSummary.FrozenPositionsValueKr, Is.EqualTo(10_000m));
            Assert.That(result.CapitalSummary.CashAvailableKr, Is.EqualTo(50_000m));
            Assert.That(result.CapitalSummary.PortfolioValueKr, Is.EqualTo(100_000m));
            Assert.That(result.CapitalSummary.CashFloorKr, Is.EqualTo(5_000m));
            Assert.That(result.Trades.Select(t => t.Isin.Value), Does.Not.Contain(frozen.Name));
        });
    }

    [Test]
    public void RunInMemory_MacroRegimeWithOverrideOn_RaisesTheFloor()
    {
        // Arrange
        var config = new PortfolioConstructorConfig
        {
            Meta       = new ConfigMeta { ConfigVersion = PortfolioConstructorConfig.ExpectedConfigVersion },
            CashPolicy = new CashPolicy { MacroOverrideEnabled = true },
        };

        // Act
        var result = CreateSut().RunInMemory(Week, [], [], 100_000m, config, macroRegime: "Crisis");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.CapitalSummary.CashPolicy.FloorPct, Is.EqualTo(0.15m));
            Assert.That(result.CapitalSummary.CashPolicy.RegimeOverrideActive, Is.True);
            Assert.That(result.CapitalSummary.CashPolicy.RegimeUsed, Is.EqualTo("Crisis"));
        });
    }

    [Test]
    public void RunInMemory_SameInputsAsTheEnvelopeCall_ProducesTheSameTrades()
    {
        // Arrange — a sell, a rotation pair, a held fund over the concentration cap and a
        // writeoff; the domain-input call must not drift from the runner's envelope call.
        var sell = MakeFund("LU0SELL", Recommendation.ThesisExit, conviction: 0.85m,
            currentlyHeld: true, currentValueKr: 25_000m, thesis: ThesisValidity.Invalid,
            category: "Branschfond, Energi", rotationPairId: "rot_2026-W46_a");
        var buy = MakeFund("LU0BUY", Recommendation.CatalystEntry, conviction: 0.78m,
            category: "Branschfond, Förnybar Energi", rotationPairId: "rot_2026-W46_a");
        var big = MakeFund("LU0BIG", Recommendation.Maintain, conviction: 0.50m,
            currentlyHeld: true, currentValueKr: 30_000m, category: "Sverigefond");
        var frozen = new FrozenPosition { Name = "Old writeoff", CurrentValueKr = 182m, Reason = "writeoff" };
        FundRecord[] funds = [sell, buy, big];
        const decimal cash = 50_000m;

        var envelope = new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = Week.Value,
            Family          = "synthetic",
            RunId           = new PipelineRunId("test-run"),
            ConfigVersion   = PortfolioConstructorConfig.ExpectedConfigVersion,
            Funds           = funds,
            FrozenPositions = [frozen],
            CashAvailableKr = cash,
            DataQuality     = new DataQuality(),
        };
        var sut = CreateSut();
        var expected = sut.RunInMemory(envelope);

        // Act
        var result = sut.RunInMemory(Week, funds, [frozen], cash, PortfolioConstructorConfig.Default);

        // Assert — everything but the timestamp.
        Assert.Multiple(() =>
        {
            Assert.That(Json(result.Trades), Is.EqualTo(Json(expected.Trades)));
            Assert.That(Json(result.RejectedRecommendations), Is.EqualTo(Json(expected.RejectedRecommendations)));
            Assert.That(Json(result.CapitalSummary), Is.EqualTo(Json(expected.CapitalSummary)));
            Assert.That(Json(result.ConstraintViolations), Is.EqualTo(Json(expected.ConstraintViolations)));
            Assert.That(result.ConstraintViolations,
                Has.Some.Matches<ConstraintViolation>(v => v.ViolationType == "position_exceeds_concentration_cap"));
        });
    }

    [Test]
    public void RunInMemory_NullFunds_Throws()
    {
        // Act & Assert
        Assert.That(() => CreateSut().RunInMemory(Week, null!, [], 0m, PortfolioConstructorConfig.Default),
            Throws.ArgumentNullException);
    }

    [Test]
    public void RunInMemory_NullConfig_Throws()
    {
        // Act & Assert
        Assert.That(() => CreateSut().RunInMemory(Week, [], [], 0m, null!),
            Throws.ArgumentNullException);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions.Default);

    private static FundRecord MakeFund(
        string isin,
        Recommendation recommendation,
        decimal conviction,
        bool currentlyHeld = false,
        decimal? currentValueKr = null,
        ThesisValidity thesis = ThesisValidity.Valid,
        string category = "Globalfond",
        string? rotationPairId = null) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin, category),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = currentlyHeld,
        CurrentValueKr = currentValueKr,
        CostBasisKr    = currentValueKr,
        Layer          = FundLayer.Active,
        Metrics        = null,
        Signal         = SignalLabel.Neutral,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = MacroAlignment.None,
        MatchedTheme      = null,
        PromotedToForming = false,
        PromotionReason   = null,
        Catalyst          = null,
        ThesisValidity    = thesis,
        ThesisRationale   = "synthetic",
        ThesisMethod      = ThesisMethod.Matrix,
        Recommendation    = recommendation,
        RecommendationReason = $"synthetic for {recommendation}",
        ConvictionScore   = conviction,
        ConvictionBreakdown = new ConvictionBreakdown
        {
            SignalStrength  = 0m, MetricsQuality = 0m, MacroAlignment = 0m,
            ThesisValidity  = 0m, UniverseContext = 0m,
        },
        UniverseRank      = null,
        Alternatives      = null,
        RotationPairId    = rotationPairId,
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
}
