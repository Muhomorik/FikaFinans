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
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Portfolio;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.PortfolioConstructor;

[TestFixture]
[TestOf(typeof(PortfolioConstructorAgent))]
public sealed class PortfolioConstructorAgentTests
{
    private IFixture _fixture = null!;
    private PortfolioConstructorAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _sut     = new PortfolioConstructorAgent(new TestPathsService());
    }

    #region Conversion + conviction gating

    [Test]
    public void RunInMemory_CleanChain_FourTradesNoViolations()
    {
        // Arrange — 1 Sell + 3 Buys + 1 Skip, diverse categories so neither
        // sector cap nor concentration cap fires.
        var sell = MakeFund("LU0SELL",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Invalid, macro: MacroAlignment.Strong,
            currentlyHeld: true, currentValueKr: 30_000m, conviction: 0.85m,
            category: "Branschfond, Energi");
        var buy1 = MakeFund("LU0BUY1",
            recommendation: Recommendation.CatalystEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Valid, macro: MacroAlignment.Strong, conviction: 0.78m,
            category: "Branschfond, Förnybar Energi");
        var buy2 = MakeFund("LU0BUY2",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.55m,
            category: "Tillväxtmarknadsfond");
        var buy3 = MakeFund("LU0BUY3",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.45m,
            category: "Sverigefond");
        var skip = MakeFund("LU0SKIP",
            recommendation: Recommendation.Skip, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None, conviction: 0.10m,
            category: "Räntefond");

        // Act
        var result = _sut.RunInMemory(MakeOutput(50_000m, sell, buy1, buy2, buy3, skip));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades, Has.Count.EqualTo(5));  // 4 active + 1 NoOp Skip
            Assert.That(result.Trades.Count(t => t.TradeType is TradeType.Buy or TradeType.TopUp), Is.EqualTo(3));
            Assert.That(result.Trades.Count(t => t.TradeType is TradeType.Sell), Is.EqualTo(1));
            Assert.That(result.RejectedRecommendations, Is.Empty);
            Assert.That(result.ConstraintViolations, Is.Empty);
            Assert.That(result.CapitalSummary.CashRemainingKr, Is.GreaterThanOrEqualTo(0m));
        });
    }

    [Test]
    public void RunInMemory_ConvictionBelowFloor_SellRejected()
    {
        // Arrange — Sell with conviction 0.31 and Partial thesis — gated.
        var sell = MakeFund("LU01",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial,
            currentlyHeld: true, currentValueKr: 20_000m, conviction: 0.31m);

        // Act
        var result = _sut.RunInMemory(MakeOutput(20_000m, sell));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Count(t => t.TradeType is TradeType.Sell), Is.Zero);
            Assert.That(result.RejectedRecommendations, Has.Count.EqualTo(1));
            Assert.That(result.RejectedRecommendations[0].Isin.Value, Is.EqualTo("LU01"));
            Assert.That(result.RejectedRecommendations[0].RejectedBecause, Does.Contain("conviction_below_floor"));
        });
    }

    [Test]
    public void RunInMemory_ConvictionBelowFloor_InvalidThesisOverride_SellExecutes()
    {
        // Arrange — Sell with conviction 0.31 but Invalid thesis — exception
        // clause: executes regardless of conviction.
        var sell = MakeFund("LU02",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Invalid, macro: MacroAlignment.Partial,
            currentlyHeld: true, currentValueKr: 20_000m, conviction: 0.31m);

        // Act
        var result = _sut.RunInMemory(MakeOutput(20_000m, sell));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades, Has.Some.Matches<Trade>(t => t.TradeType == TradeType.Sell));
            Assert.That(result.Trades.First(t => t.TradeType == TradeType.Sell).AmountKr, Is.EqualTo(20_000m));
            Assert.That(result.RejectedRecommendations, Is.Empty);
        });
    }

    [Test]
    public void RunInMemory_ConvictionBelowFloor_BuyRejected()
    {
        // Arrange — Buy with conviction 0.20 — gated.
        var buy = MakeFund("LU03",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.None, conviction: 0.20m);

        // Act
        var result = _sut.RunInMemory(MakeOutput(50_000m, buy));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Count(t => t.TradeType is TradeType.Buy), Is.Zero);
            Assert.That(result.RejectedRecommendations, Has.Count.EqualTo(1));
            Assert.That(result.RejectedRecommendations[0].RejectedBecause, Does.Contain("conviction_below_floor"));
        });
    }

    [Test]
    public void RunInMemory_HighConvictionSellFiltered_RaisesUnusualViolation()
    {
        // Arrange — sell with conviction 0.85 + thesis = Valid (which scores 0
        // for thesis_validity ... but conviction itself is ≥ 0.4 so it passes
        // the gate). Hmm, to actually trigger the unusual-flag branch we need
        // conviction ≥ 0.8 AND it's filtered, which only happens when conviction
        // < skip_sell_below_conviction. The branch is unreachable unless
        // someone tweaks config thresholds upward; fabricate that scenario:
        var config = new PortfolioConstructorConfig
        {
            Guards = new ConvictionGuards
            {
                SkipSellBelowConviction = 0.90m,                  // raise floor above 0.85
                SkipSellBelowConvictionUnlessThesisInvalid = true,
                SkipBuyBelowConviction = 0.30m,
            },
        };
        var sut = new PortfolioConstructorAgent(new TestPathsService(), config);
        var sell = MakeFund("LU04",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Strong,
            currentlyHeld: true, currentValueKr: 20_000m, conviction: 0.85m);

        // Act
        var result = sut.RunInMemory(MakeOutput(20_000m, sell));

        // Assert
        Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "conviction_gate_unusual"), Is.True);
    }

    #endregion

    #region Sizing

    [Test]
    public void RunInMemory_HeldNearCap_ToppedUpBuyDropsBelowMin()
    {
        // Arrange — held position 3.5k of an 80k portfolio sits at 4.4%
        // weight (below the 5% TopUp target so TopUp is emitted), while its
        // concentration headroom (8k cap − 3.5k current = 4.5k) falls below
        // min_trade 5k → dropped. Three fresh buys each headroom-cap at 8k
        // and execute.
        var nearCap = MakeFund("LU0NEAR",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.55m,
            currentlyHeld: true, currentValueKr: 3_500m,
            category: "Sverigefond");
        var fresh1 = MakeFund("LU0F1",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.50m,
            category: "Branschfond, Energi");
        var fresh2 = MakeFund("LU0F2",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.45m,
            category: "Tillväxtmarknadsfond");
        var fresh3 = MakeFund("LU0F3",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.40m,
            category: "Globalfond");

        var result = _sut.RunInMemory(MakeOutput(76_500m, nearCap, fresh1, fresh2, fresh3));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Count(t => t.TradeType is TradeType.Buy or TradeType.TopUp),
                Is.EqualTo(3));
            Assert.That(result.RejectedRecommendations.Count(r => r.Isin == "LU0NEAR"), Is.EqualTo(1));
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "min_trade_dropout"), Is.True);
            Assert.That(result.Trades.Where(t => t.TradeType is TradeType.Buy).All(t => t.AmountKr >= 5_000m), Is.True);
        });
    }

    [Test]
    public void RunInMemory_CashBugRegression_NoNegativeCashRemaining()
    {
        // Arrange — the original chain bug: 3 Buys with default-pct sizing
        // implied 60_000 kr drawn from 50_000 cash. Sizing must constrain.
        var buys = new[]
        {
            MakeFund("LU0EM", recommendation: Recommendation.MomentumEntry,
                signal: SignalLabel.Strength, thesis: ThesisValidity.Partial,
                macro: MacroAlignment.Partial, conviction: 0.55m),
            MakeFund("LU0OPP", recommendation: Recommendation.MomentumEntry,
                signal: SignalLabel.Strength, thesis: ThesisValidity.Partial,
                macro: MacroAlignment.Partial, conviction: 0.50m),
            MakeFund("LU0QUAL", recommendation: Recommendation.MomentumEntry,
                signal: SignalLabel.Strength, thesis: ThesisValidity.Partial,
                macro: MacroAlignment.Partial, conviction: 0.45m),
        };

        // Act — only 50_000 kr cash, no held positions.
        var result = _sut.RunInMemory(MakeOutput(50_000m, buys));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.CapitalSummary.CashRemainingKr, Is.GreaterThanOrEqualTo(0m));
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "cash_remaining_kr_negative"), Is.False);
            Assert.That(result.CapitalSummary.TotalBuyAmountKr,
                Is.LessThanOrEqualTo(result.CapitalSummary.TotalDeployableKr + 0.01m));
        });
    }

    #endregion

    #region Rotation pair

    [Test]
    public void RunInMemory_RotationPairBothSidesExecute_BothInTrades()
    {
        // Arrange — paired Sell + Buy in different categories so neither cap
        // intercepts.
        var sell = MakeFund("LU0PSELL",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Invalid, macro: MacroAlignment.Strong,
            currentlyHeld: true, currentValueKr: 25_000m, conviction: 0.91m,
            category: "Branschfond, Energi", rotationPairId: "rot_2026-W18_a");
        var buy = MakeFund("LU0PBUY",
            recommendation: Recommendation.CatalystEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Valid, macro: MacroAlignment.Strong, conviction: 0.78m,
            category: "Branschfond, Förnybar Energi", rotationPairId: "rot_2026-W18_a");

        // Act
        var result = _sut.RunInMemory(MakeOutput(50_000m, sell, buy));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Any(t => t.Isin == "LU0PSELL" && t.TradeType == TradeType.Sell), Is.True);
            Assert.That(result.Trades.Any(t => t.Isin == "LU0PBUY"  && t.TradeType == TradeType.Buy),  Is.True);
            Assert.That(result.Trades.All(t => t.RotationPairId == "rot_2026-W18_a"
                                              || t.RotationPairId is null), Is.True);
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "rotation_pair_partial"), Is.False);
        });
    }

    [Test]
    public void RunInMemory_RotationPairBuyFailsSectorCap_PartialViolation()
    {
        // Arrange — Energy sector already at 250k of 400k portfolio (62.5%);
        // paired Buy in same sector would push further over the 30% cap, so
        // Buy is refused. Concentration cap is raised in this test config to
        // isolate the sector path.
        var config = new PortfolioConstructorConfig
        {
            Constraints = new PortfolioConstraints
            {
                MaxPositionPctOfPortfolio = 1.00m,
                MaxSectorPctOfPortfolio   = 0.30m,
                MinTradeKr                = 5_000m,
            },
        };
        var sut = new PortfolioConstructorAgent(new TestPathsService(), config);

        var sell = MakeFund("LU0RSELL",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Invalid, macro: MacroAlignment.Strong,
            currentlyHeld: true, currentValueKr: 50_000m, conviction: 0.85m,
            category: "Globalfond", rotationPairId: "rot_2026-W18_a");
        var heldInSector = MakeFund("LU0HELD",
            recommendation: Recommendation.Maintain, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None,
            currentlyHeld: true, currentValueKr: 250_000m, conviction: 0.10m,
            category: "Branschfond, Energi");
        var pairedBuy = MakeFund("LU0PBUYX",
            recommendation: Recommendation.CatalystEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Valid, macro: MacroAlignment.Strong, conviction: 0.80m,
            category: "Branschfond, Energi", rotationPairId: "rot_2026-W18_a");

        var result = sut.RunInMemory(MakeOutput(100_000m, sell, heldInSector, pairedBuy));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.RejectedRecommendations.Any(r => r.Isin == "LU0PBUYX"), Is.True);
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "rotation_pair_partial"), Is.True);
            Assert.That(result.Trades.Any(t => t.Isin == "LU0RSELL" && t.TradeType == TradeType.Sell), Is.True);
        });
    }

    #endregion

    #region Concentration cap

    [Test]
    public void RunInMemory_HeldPositionAboveCap_TrimEmitted()
    {
        // Arrange — held fund worth 12% of portfolio (cap 10%).
        var concentrated = MakeFund("LU0CONC",
            recommendation: Recommendation.Maintain, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None,
            currentlyHeld: true, currentValueKr: 12_000m, conviction: 0.20m);
        var filler = MakeFund("LU0FILL",
            recommendation: Recommendation.Skip, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None,
            currentlyHeld: true, currentValueKr: 80_000m, conviction: 0.10m,
            category: "Räntefond");
        // Portfolio = 12k + 80k + 8k cash = 100k. Cap @ 10% = 10k. Trim 2k.
        var result = _sut.RunInMemory(MakeOutput(8_000m, concentrated, filler));

        // Assert
        Assert.Multiple(() =>
        {
            var trim = result.Trades.FirstOrDefault(t => t.Isin == "LU0CONC" && t.TradeType == TradeType.Trim);
            Assert.That(trim, Is.Not.Null);
            Assert.That(trim!.TrimReason, Is.EqualTo("concentration_cap"));
            Assert.That(trim.AmountKr, Is.EqualTo(2_000m));
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "position_exceeds_concentration_cap"),
                Is.True);
        });
    }

    #endregion

    #region Layer enforcement

    [Test]
    public void RunInMemory_CoreFundThesisExit_SuppressedAsHold()
    {
        // Arrange — core fund recommended ThesisExit.
        var core = MakeFund("LU0CORE",
            recommendation: Recommendation.ThesisExit, signal: SignalLabel.Weakness,
            thesis: ThesisValidity.Invalid, macro: MacroAlignment.Strong,
            currentlyHeld: true, currentValueKr: 30_000m, conviction: 0.85m,
            layer: FundLayer.Core);

        // Act
        var result = _sut.RunInMemory(MakeOutput(20_000m, core));

        // Assert
        Assert.Multiple(() =>
        {
            var trade = result.Trades.Single(t => t.Isin == "LU0CORE");
            Assert.That(trade.TradeType, Is.EqualTo(TradeType.Hold));
            Assert.That(trade.TradeReason, Is.EqualTo("core_locked"));
            Assert.That(result.RejectedRecommendations.Single().RejectedBecause, Is.EqualTo("core_locked"));
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "core_locked_recommendation_suppressed"),
                Is.True);
        });
    }

    [Test]
    public void RunInMemory_CoreFundConcentrationBreach_TrimSuppressed()
    {
        // Arrange — core fund holding 12k out of 100k portfolio.
        var core = MakeFund("LU0CCORE",
            recommendation: Recommendation.Maintain, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None,
            currentlyHeld: true, currentValueKr: 12_000m, conviction: 0.20m,
            layer: FundLayer.Core);
        var filler = MakeFund("LU0CFILL",
            recommendation: Recommendation.Skip, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None,
            currentlyHeld: true, currentValueKr: 80_000m, conviction: 0.10m,
            category: "Räntefond");

        // Act — 8k cash, 92k held → 100k portfolio, cap 10k, core at 12k breaches
        var result = _sut.RunInMemory(MakeOutput(8_000m, core, filler));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Any(t => t.Isin == "LU0CCORE" && t.TradeType == TradeType.Trim), Is.False);
            Assert.That(result.ConstraintViolations.Any(v =>
                v.ViolationType == "core_locked_recommendation_suppressed" && v.Isin == "LU0CCORE"), Is.True);
        });
    }

    [Test]
    public void RunInMemory_CoreFundCatalystEntry_BuyExecutes()
    {
        // Arrange — core funds CAN buy/topup.
        var core = MakeFund("LU0COREBUY",
            recommendation: Recommendation.CatalystEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Valid, macro: MacroAlignment.Strong, conviction: 0.78m,
            layer: FundLayer.Core);

        // Act
        var result = _sut.RunInMemory(MakeOutput(50_000m, core));

        // Assert — single buy executed, no rejections, no violations.
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Count(t => t.TradeType == TradeType.Buy), Is.EqualTo(1));
            Assert.That(result.RejectedRecommendations, Is.Empty);
            Assert.That(result.ConstraintViolations, Is.Empty);
        });
    }

    #endregion

    #region Frozen positions + cash policy

    [Test]
    public void RunInMemory_FrozenPosition_IncludedInPortfolioValue_NotInTrades()
    {
        // Arrange — single frozen 182 kr position, no funds.
        var input = MakeOutput(50_000m, Array.Empty<FundRecord>(),
            new[]
            {
                new FrozenPosition
                {
                    Name = "Liquidated Fund A",
                    Isin = "LU0FROZE",
                    CurrentValueKr = 182m,
                    CostBasisKr    = 5_000m,
                    Reason         = "writeoff_liquidated",
                },
            });

        // Act
        var result = _sut.RunInMemory(input);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades.Any(t => t.Isin == "LU0FROZE"), Is.False);
            Assert.That(result.CapitalSummary.FrozenPositionsValueKr, Is.EqualTo(182m));
            Assert.That(result.CapitalSummary.PortfolioValueKr, Is.EqualTo(50_182m));
        });
    }

    [Test]
    public void RunInMemory_MacroOverrideEnabled_RaisesFloor()
    {
        // Arrange — Crisis regime with override on raises floor to 15%.
        var config = new PortfolioConstructorConfig
        {
            CashPolicy = new CashPolicy
            {
                FloorPct             = 0.05m,
                MacroOverrideEnabled = true,
                MacroOverrideTable   = new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["Crisis"]      = 0.15m,
                    ["Stagflation"] = 0.10m,
                    ["Mixed"]       = 0.07m,
                    ["Risk-on"]     = 0.05m,
                },
            },
        };
        var sut = new PortfolioConstructorAgent(new TestPathsService(), config);

        var buy = MakeFund("LU0CBUY",
            recommendation: Recommendation.MomentumEntry, signal: SignalLabel.Strength,
            thesis: ThesisValidity.Partial, macro: MacroAlignment.Partial, conviction: 0.50m);

        // 100k cash → 15% floor = 15k → deployable 85k. Single Buy capped at
        // max_position = 10% × 100k = 10k.
        var result = sut.RunInMemory(MakeOutput(100_000m, new[] { buy }, Array.Empty<FrozenPosition>()), "Crisis");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.CapitalSummary.CashPolicy.FloorPct, Is.EqualTo(0.15m));
            Assert.That(result.CapitalSummary.CashPolicy.RegimeOverrideActive, Is.True);
            Assert.That(result.CapitalSummary.CashFloorKr, Is.EqualTo(15_000m));
            Assert.That(result.CapitalSummary.CashAboveFloorKr, Is.EqualTo(85_000m));
            var buyTrade = result.Trades.Single(t => t.TradeType == TradeType.Buy);
            Assert.That(buyTrade.AmountKr, Is.EqualTo(10_000m));
            Assert.That(result.CapitalSummary.CashRemainingKr, Is.EqualTo(90_000m));
        });
    }

    #endregion

    #region Failure modes / config

    [Test]
    public void Constructor_WrongConfigVersion_Throws()
    {
        // Arrange
        var bad = new PortfolioConstructorConfig
        {
            Meta = new ConfigMeta { ConfigVersion = "0.9.0" },
        };

        // Act + Assert
        Assert.That(() => new PortfolioConstructorAgent(new TestPathsService(), bad),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RunInMemory_HeldFundWithNullCurrentValue_Throws()
    {
        // Arrange
        var bad = MakeFund("LU0BAD",
            recommendation: Recommendation.Maintain, signal: SignalLabel.Neutral,
            thesis: ThesisValidity.NotApplicable, macro: MacroAlignment.None,
            currentlyHeld: true, currentValueKr: null, conviction: 0.10m);

        // Act + Assert
        Assert.That(() => _sut.RunInMemory(MakeOutput(0m, bad)),
            Throws.TypeOf<InvalidDataException>());
    }

    #endregion

    #region Disk happy path

    [Test]
    public void Run_RealHappyPathFixture_ProducesTradesJson()
    {
        // Arrange — cascade fixtures up to step 09, run step 10.
        const string runId = "test-step10-happypath";
        EnsureUniverseEnricherFixtureExists(runId);

        // Act
        var result = _sut.Run("2026-W18", runId);

        // Assert
        var outPath = Paths.PortfolioConstructorOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<TradesOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Trades, Is.Not.Empty);
            Assert.That(roundTripped!.Trades, Has.Count.EqualTo(result.Trades.Count));
            Assert.That(result.CapitalSummary.PortfolioValueKr, Is.GreaterThan(0m));
            Assert.That(result.CapitalSummary.CashRemainingKr, Is.GreaterThanOrEqualTo(0m));
            Assert.That(result.ConstraintViolations.Any(v => v.ViolationType == "cash_remaining_kr_negative"), Is.False);
        });

        var json = File.ReadAllText(outPath);
        Assert.That(json, Does.Contain("\"trades\":"));
        Assert.That(json, Does.Contain("\"capital_summary\":"));
        Assert.That(json, Does.Contain("\"cash_floor_kr\":"));
    }

    private static void EnsureUniverseEnricherFixtureExists(string runId)
    {
        var step9Path = Paths.UniverseEnricherOutput("2026-W18", runId);
        if (File.Exists(step9Path)) return;

        var step8Path = Paths.RecommenderOutput("2026-W18", runId);
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
            File.WriteAllText(step3Path, JsonSerializer.Serialize(synthetic, JsonOptions.Default));
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
            new MacroAlignerAgent(new TestPathsService(), alignLlm.Object).RunAsync("2026-W18", runId).GetAwaiter().GetResult();
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
            new CatalystTaggerAgent(new TestPathsService(), taggerLlm.Object).RunAsync("2026-W18", runId).GetAwaiter().GetResult();
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
            new ThesisValidatorAgent(new TestPathsService(), thesisLlm.Object).RunAsync("2026-W18", runId).GetAwaiter().GetResult();
        }
        if (!File.Exists(step8Path))
        {
            new RecommenderAgent(new TestPathsService()).Run("2026-W18", runId);
        }

        var diffLlm = new Mock<IDifferentiatorLlmClient>();
        diffLlm
            .Setup(x => x.WriteDifferentiatorsAsync(
                It.IsAny<DifferentiatorRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DifferentiatorRequest req, CancellationToken _) =>
                req.Alternatives.Select(a => new DifferentiatorLine
                {
                    Isin           = a.Isin,
                    Differentiator = $"Stub differentiator for {a.Metadata.Name}.",
                }).ToArray());
        new UniverseEnricherAgent(new TestPathsService(), diffLlm.Object).RunAsync("2026-W18", runId).GetAwaiter().GetResult();
    }

    private static MacroContext MakeSyntheticMacroContext(string isoWeek) => new()
    {
        GeneratedAt      = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek          = isoWeek,
        ConfigVersion    = "1.0.0",
        SourceRunIds     = new SourceRunIds
        {
            WeeklySummaryRunId     = "synthetic-ws",
            SubstitutionChainRunId = "synthetic-sc",
            RotationTargetsRunId   = "synthetic-rt",
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
        decimal conviction,
        bool currentlyHeld = false,
        decimal? currentValueKr = null,
        FundLayer layer = FundLayer.Active,
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
        Layer          = layer,
        Metrics        = null,
        Signal         = signal,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = macro,
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

    private static DataLoaderOutput MakeOutput(decimal cashKr, params FundRecord[] funds) =>
        MakeOutput(cashKr, funds, Array.Empty<FrozenPosition>());

    private static DataLoaderOutput MakeOutput(
        decimal cashKr,
        IReadOnlyList<FundRecord> funds,
        IReadOnlyList<FrozenPosition> frozen) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = "2026-W18",
        Family          = "synthetic",
        RunId           = "test-run",
        ConfigVersion   = "1.0.0",
        Funds           = funds,
        FrozenPositions = frozen,
        CashAvailableKr = cashKr,
        DataQuality     = new DataQuality(),
    };

    #endregion
}
