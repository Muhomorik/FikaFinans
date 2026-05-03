using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;
using FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;

namespace FikaFinans.InfrastructureV2.Tests.Agents.SignalScorer;

[TestFixture]
[TestOf(typeof(SignalScorerAgent))]
public class SignalScorerAgentTests
{
    private IFixture _fixture = null!;
    private SignalScorerConfig _config = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _config = SignalScorerConfig.Default;
    }

    #region Strength

    [Test]
    public void RunInMemory_AllBuyCriteriaMet_StrengthBuy3Of3ZeroDd()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(windowsPositive: 3, windowsTotal: 3, currentDd: 0m, sharpe12w: 1.5m, sharpe2w: 1.0m);
        var input = MakeOutput(MakeFund("LU0001", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Strength));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.Buy3of3ZeroDd));
            Assert.That(fund.CriteriaEvaluation!.Buy3Of3Passed, Is.True);
            Assert.That(fund.CriteriaEvaluation.BuyMaxDdPassed, Is.True);
            Assert.That(fund.CriteriaEvaluation.BuyMinSharpe12wPassed, Is.True);
            Assert.That(fund.CriteriaEvaluation.MissingForUpgrade, Is.Null);
        });
    }

    [Test]
    public void RunInMemory_NanSharpe2wTreatedAsZero_AllBuyMet_StrengthWithWarning()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(
            windowsPositive: 3, windowsTotal: 3,
            currentDd: 0m, sharpe12w: 1.5m,
            sharpe2w: null, sharpe2wIsNan: true);
        var input = MakeOutput(MakeFund("LU0007", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Strength));
            Assert.That(fund.CriteriaEvaluation!.SellSharpe2wLt0, Is.False);
            Assert.That(fund.CriteriaEvaluation.DataQualityWarnings,
                Has.Member("sharpe_2w_nan_treated_as_zero"));
        });
    }

    #endregion

    #region Weakness

    [Test]
    public void RunInMemory_DeepDrawdownAndNegativeSharpe_WeaknessSellCombined()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(
            windowsPositive: 2, windowsTotal: 3,
            currentDd: -5.5m, sharpe12w: 0.2m, sharpe2w: -3.5m);
        var input = MakeOutput(MakeFund("LU0002", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Weakness));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.SellCombined));
            Assert.That(fund.CriteriaEvaluation!.SellSharpe2wLt0, Is.True);
            Assert.That(fund.CriteriaEvaluation.SellDdLtThreshold, Is.True);
            Assert.That(fund.CriteriaEvaluation.SellPosLe1, Is.False);
        });
    }

    [Test]
    public void RunInMemory_OnlyOnePositiveWindow_WeaknessSellPosLe1()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        // pos 1/3, modest dd (-2.0% breaches -1.5 threshold), sharpe_2w slightly negative
        // → both sell_pos_le_1 AND sell_drawdown_breach AND sell_sharpe_negative fire → sell_combined
        var metrics = MakeMetrics(
            windowsPositive: 1, windowsTotal: 3,
            currentDd: -2.0m, sharpe12w: 0m, sharpe2w: -0.5m);
        var input = MakeOutput(MakeFund("LU0003", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Weakness));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.SellCombined));
            Assert.That(fund.CriteriaEvaluation!.SellPosLe1, Is.True);
        });
    }

    [Test]
    public void RunInMemory_OnlySharpe2wNegative_WeaknessSellSharpeNegative()
    {
        // Arrange — single sell trigger fires (sharpe_2w < 0) but not the others
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(
            windowsPositive: 2, windowsTotal: 3,
            currentDd: -0.5m, sharpe12w: 0.3m, sharpe2w: -0.2m);
        var input = MakeOutput(MakeFund("LU0004", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Weakness));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.SellSharpeNegative));
            Assert.That(fund.CriteriaEvaluation!.SellSharpe2wLt0, Is.True);
            Assert.That(fund.CriteriaEvaluation.SellDdLtThreshold, Is.False);
            Assert.That(fund.CriteriaEvaluation.SellPosLe1, Is.False);
        });
    }

    #endregion

    #region Neutral

    [Test]
    public void RunInMemory_TaiwanFalsePositiveGuard_NeutralDefault()
    {
        // Arrange — pos 2/3, dd 0, sharpe_2w +8 → no sell trigger fires, but missing one buy criterion.
        // Under the old all-of rule this would have fired Weakness; the any-of rule keeps it Neutral.
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(
            windowsPositive: 2, windowsTotal: 3,
            currentDd: 0m, sharpe12w: 1.0m, sharpe2w: 8.5m);
        var input = MakeOutput(MakeFund("LU0005", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Neutral));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.NeutralDefault));
            Assert.That(fund.CriteriaEvaluation!.MissingForUpgrade, Is.EqualTo("3rd_positive_window"));
        });
    }

    [Test]
    public void RunInMemory_TwoBucketsOnly_NeutralInsufficientHistory()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(
            windowsPositive: 0, windowsTotal: 2,
            currentDd: 0m, sharpe12w: null, sharpe2w: 0.1m);
        var input = MakeOutput(MakeFund("LU0006", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Neutral));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.NeutralInsufficient));
            Assert.That(fund.CriteriaEvaluation!.MissingForUpgrade, Is.Null);
        });
    }

    [Test]
    public void RunInMemory_BuyCriteriaMetButSellTriggerFires_NeutralConflicting()
    {
        // Arrange — all 3 buy criteria pass (pos 3/3, dd 0, sharpe_12w 1.5) but sharpe_2w is -0.5 → conflict.
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(
            windowsPositive: 3, windowsTotal: 3,
            currentDd: 0m, sharpe12w: 1.5m, sharpe2w: -0.5m);
        var input = MakeOutput(MakeFund("LU0008", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Signal, Is.EqualTo(SignalLabel.Neutral));
            Assert.That(fund.RuleFired, Is.EqualTo(RuleFired.NeutralConflicting));
            Assert.That(fund.CriteriaEvaluation!.SellSharpe2wLt0, Is.True);
            Assert.That(fund.CriteriaEvaluation.Buy3Of3Passed, Is.True);
        });
    }

    [Test]
    public void RunInMemory_FundWithoutMetrics_NeutralNoData()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        var fund = new FundRecord
        {
            Isin           = "LU0009",
            Metadata       = MakeMetadata("LU0009"),
            NavBuckets     = Array.Empty<NavBucket>(),
            Snapshot       = null,
            CurrentlyHeld  = false,
            CurrentValueKr = null,
            CostBasisKr    = null,
            Layer          = FundLayer.Active,
            Metrics        = null,
        };
        var input = MakeOutput(fund);

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var enriched = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Signal, Is.EqualTo(SignalLabel.Neutral));
            Assert.That(enriched.RuleFired, Is.EqualTo(RuleFired.NeutralNoData));
            Assert.That(enriched.CriteriaEvaluation!.DataQualityWarnings,
                Has.Member("metrics_missing"));
        });
    }

    #endregion

    #region Append-only and JSON round-trip

    [Test]
    public void RunInMemory_PreservesPriorFields_AppendOnlyInvariant()
    {
        // Arrange
        var sut = _fixture.Create<SignalScorerAgent>();
        var metrics = MakeMetrics(windowsPositive: 3, windowsTotal: 3, currentDd: 0m, sharpe12w: 1.5m, sharpe2w: 1.0m);
        var input = MakeOutput(MakeFund("LU0010", metrics));

        // Act
        var result = sut.RunInMemory(input, _config);

        // Assert
        var fund = result.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.Isin, Is.EqualTo("LU0010"));
            Assert.That(fund.Metadata, Is.SameAs(input.Funds[0].Metadata));
            Assert.That(fund.Metrics, Is.SameAs(metrics));
            Assert.That(result.IsoWeek, Is.EqualTo(input.IsoWeek));
            Assert.That(result.CashAvailableKr, Is.EqualTo(input.CashAvailableKr));
            Assert.That(result.FrozenPositions, Is.SameAs(input.FrozenPositions));
        });
    }

    [Test]
    public void Run_RealHappyPathFixture_ProducesSignalEnrichedJson()
    {
        // Arrange
        const string runId = "test-happypath";
        EnsureMetricsFixtureExists(runId);
        var sut = _fixture.Create<SignalScorerAgent>();

        // Act
        var result = sut.Run("2026-W18", runId);

        // Assert
        var outPath = Paths.SignalScorerOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Has.Count.EqualTo(16));
            Assert.That(result.Funds.All(f => f.Signal is not null), Is.True);
            Assert.That(result.Funds.All(f => !string.IsNullOrEmpty(f.RuleFired)), Is.True);
            Assert.That(result.Funds.All(f => f.CriteriaEvaluation is not null), Is.True);
            // Append-only — step 02 fields preserved.
            Assert.That(roundTripped!.Funds.All(f => f.Metrics is not null), Is.True);
            Assert.That(roundTripped.Funds[0].Metadata, Is.Not.Null);
            Assert.That(roundTripped.Funds[0].NavBuckets, Is.Not.Empty);
        });
    }

    [Test]
    public void Run_RealHappyPathFixture_JsonContainsExpectedSnakeCaseFields()
    {
        // Arrange — mostly to catch the buy_3of3_passed override regression
        const string runId = "test-happypath";
        EnsureMetricsFixtureExists(runId);
        var sut = _fixture.Create<SignalScorerAgent>();
        sut.Run("2026-W18", runId);

        // Act
        var json = File.ReadAllText(Paths.SignalScorerOutput("2026-W18", runId));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"signal\":"));
            Assert.That(json, Does.Contain("\"rule_fired\":"));
            Assert.That(json, Does.Contain("\"criteria_evaluation\":"));
            Assert.That(json, Does.Contain("\"buy_3of3_passed\":"));
            Assert.That(json, Does.Contain("\"sell_sharpe_2w_lt_0\":"));
            Assert.That(json, Does.Contain("\"sell_pos_le_1\":"));
            Assert.That(json, Does.Contain("\"data_quality_warnings\":"));
            Assert.That(json, Does.Not.Contain("buy_3of_3_passed"));
        });
    }

    #endregion

    #region Helpers

    private static void EnsureMetricsFixtureExists(string runId)
    {
        var step2Path = Paths.MetricsCalculatorOutput("2026-W18", runId);
        if (File.Exists(step2Path)) return;

        var step1Path = Paths.DataLoaderOutput("2026-W18", runId);
        if (!File.Exists(step1Path))
        {
            new FikaFinans.InfrastructureV2.Tests.Agents.DataLoader.DataLoaderAgent().Run(
                "schroder", "2026-W18", runId);
        }
        new FikaFinans.InfrastructureV2.Tests.Agents.MetricsCalculator.MetricsCalculatorAgent().Run("2026-W18", runId);
    }

    private static FundRecord MakeFund(string isin, Metrics metrics) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = metrics,
    };

    private static FundMetadata MakeMetadata(string isin) => new()
    {
        Isin                     = isin,
        Name                     = $"Test Fund {isin}",
        CompanyName              = "TestCo",
        CurrencyCode             = "SEK",
        Category                 = "Test",
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

    private static Metrics MakeMetrics(
        int windowsPositive,
        int windowsTotal,
        decimal? currentDd,
        decimal? sharpe12w,
        decimal? sharpe2w,
        bool sharpe2wIsNan = false) => new()
    {
        WindowsPositiveCount    = windowsPositive,
        WindowsTotal            = windowsTotal,
        CurrentDrawdownPct      = currentDd,
        AnnVolatility2wPct      = 12m,
        Sharpe2w                = sharpe2w,
        Sharpe12w               = sharpe12w,
        Sharpe1y                = 0.7m,
        AnnVolatility12wPct     = 14m,
        AnnVolatility1yPct      = 16m,
        Return12wCompoundPct    = 10m,
        Return1yCompoundPct     = 18m,
        MaxDrawdown12wPct       = -3m,
        MaxDrawdown1yPct        = -8m,
        TotalFeePct             = 1.0m,
        NetReturnAfterFee12wPct = 9m,
        AsOfDate                = new DateOnly(2026, 4, 28),
        DataQuality             = new MetricsDataQuality
        {
            BucketsUsed             = 26,
            SnapshotMissing         = false,
            SnapshotStaleVsSummary  = false,
            Sharpe2wIsNan           = sharpe2wIsNan,
            Sharpe12wIsNan          = false,
            Sharpe1yIsNan           = false,
        },
    };

    private static DataLoaderOutput MakeOutput(params FundRecord[] funds) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = "2026-W18",
        Family          = "test",
        RunId           = "unit-test",
        ConfigVersion   = "1.0.0",
        Funds           = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        CashAvailableKr = 0m,
        DataQuality     = new DataQuality(),
    };

    #endregion
}
