using FikaFinans.Application.Paths;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Domain.Funds;
using FikaFinans.Application.Pipeline.Configs;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MetricsCalculator;

[TestFixture]
public class MetricsCalculatorAgentTests
{
    private IFixture _fixture = null!;
    private MetricsCalculatorConfig _config = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _config = new MetricsCalculatorConfig();
    }

    // ─── #1. Standard fund: 26 buckets + snapshot → all metrics populated, no flags ───
    [Test]
    public void RunInMemory_StandardFund_AllMetricsPopulatedAndNoFlags()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 26, startDate: new DateOnly(2025, 11, 5), return2wPct: 1.5m);
        var snapshot = new FundSnapshot
        {
            AsOfDate              = buckets[^1].PeriodEnd,
            Return12wCompoundPct  = 12.0m,
            AnnVolatility12wPct   = 14.0m,
            Sharpe12w             = 1.2m,
            MaxDrawdown12wPct     = -3.5m,
            Return1yCompoundPct   = 22.0m,
            AnnVolatility1yPct    = 16.0m,
            Sharpe1y              = 0.9m,
            MaxDrawdown1yPct      = -8.0m,
        };
        var input = MakeOutput(MakeFund("LU0001", buckets, snapshot, totalFee: 2.0m));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.WindowsTotal, Is.EqualTo(3));
            Assert.That(m.WindowsPositiveCount, Is.EqualTo(3));
            Assert.That(m.Sharpe12w, Is.EqualTo(1.2m));
            Assert.That(m.Sharpe1y, Is.EqualTo(0.9m));
            Assert.That(m.Return12wCompoundPct, Is.EqualTo(12.0m));
            Assert.That(m.TotalFeePct, Is.EqualTo(2.0m));
            Assert.That(m.AsOfDate, Is.EqualTo(buckets[^1].PeriodEnd));
            Assert.That(m.DataQuality.SnapshotMissing, Is.False);
            Assert.That(m.DataQuality.SnapshotStaleVsSummary, Is.False);
            Assert.That(m.DataQuality.Sharpe2wIsNan, Is.False);
            Assert.That(m.DataQuality.Sharpe12wIsNan, Is.False);
            Assert.That(m.DataQuality.Sharpe1yIsNan, Is.False);
            Assert.That(m.DataQuality.BucketsUsed, Is.EqualTo(26));
        });
    }

    // ─── #2. New fund: 6 buckets, sharpe_1y = null → sharpe_1y_is_nan = true ─────────
    [Test]
    public void RunInMemory_NewFund_PartialOutputAndSharpe1yIsNanFlag()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 6, startDate: new DateOnly(2026, 1, 7));
        var snapshot = MakeSnapshot(buckets[^1].PeriodEnd, sharpe1y: null, return1y: null);
        var input = MakeOutput(MakeFund("LU0002", buckets, snapshot));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.Sharpe1y, Is.Null);
            Assert.That(m.Return1yCompoundPct, Is.Null);
            Assert.That(m.DataQuality.Sharpe1yIsNan, Is.True);
            Assert.That(m.DataQuality.Sharpe12wIsNan, Is.False);
            Assert.That(m.WindowsTotal, Is.EqualTo(3));
            Assert.That(m.DataQuality.BucketsUsed, Is.EqualTo(6));
        });
    }

    // ─── #3. Missing snapshot → all rolling null, snapshot_missing = true ────────────
    [Test]
    public void RunInMemory_MissingSnapshot_AllRollingFieldsNullAndFlagSet()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 26, startDate: new DateOnly(2025, 11, 5));
        var input = MakeOutput(MakeFund("LU0003", buckets, snapshot: null, totalFee: 1.5m));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.DataQuality.SnapshotMissing, Is.True);
            Assert.That(m.Sharpe12w, Is.Null);
            Assert.That(m.Sharpe1y, Is.Null);
            Assert.That(m.Return12wCompoundPct, Is.Null);
            Assert.That(m.Return1yCompoundPct, Is.Null);
            Assert.That(m.AnnVolatility12wPct, Is.Null);
            Assert.That(m.NetReturnAfterFee12wPct, Is.Null);
            Assert.That(m.AsOfDate, Is.Null);
            Assert.That(m.TotalFeePct, Is.EqualTo(1.5m));
            // 2w fields still come from latest bucket.
            Assert.That(m.Sharpe2w, Is.Not.Null);
            // *_is_nan flags are only true when snapshot exists but value is null.
            Assert.That(m.DataQuality.Sharpe12wIsNan, Is.False);
            Assert.That(m.DataQuality.Sharpe1yIsNan, Is.False);
        });
    }

    // ─── #4. Bond fund near-zero vol → sharpe_2w = NaN preserved as null ─────────────
    [Test]
    public void RunInMemory_LatestBucketSharpe2wIsNan_PreservedAsNullWithFlag()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 4, startDate: new DateOnly(2026, 2, 4));
        // Force the last bucket's Sharpe2w to null (the producer's NaN guard fired).
        var rebuilt = buckets.Take(buckets.Count - 1)
            .Append(new NavBucket
            {
                PeriodStart        = buckets[^1].PeriodStart,
                PeriodEnd          = buckets[^1].PeriodEnd,
                FirstNav           = buckets[^1].FirstNav,
                LastNav            = buckets[^1].LastNav,
                NavHigh            = buckets[^1].NavHigh,
                NavLow             = buckets[^1].NavLow,
                Return2wPct        = buckets[^1].Return2wPct,
                AnnVolatility2wPct = 0.005m,    // near-zero vol triggered the NaN
                MaxDrawdown2wPct   = buckets[^1].MaxDrawdown2wPct,
                CurrentDrawdownPct = buckets[^1].CurrentDrawdownPct,
                Sharpe2w           = null,
                BestDayPct         = buckets[^1].BestDayPct,
                WorstDayPct        = buckets[^1].WorstDayPct,
                PctPositiveDays    = buckets[^1].PctPositiveDays,
                Skewness           = buckets[^1].Skewness,
            }).ToList();

        var snapshot = MakeSnapshot(rebuilt[^1].PeriodEnd);
        var input = MakeOutput(MakeFund("LU0004", rebuilt, snapshot));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.Sharpe2w, Is.Null);
            Assert.That(m.DataQuality.Sharpe2wIsNan, Is.True);
            Assert.That(m.AnnVolatility2wPct, Is.EqualTo(0.005m));
        });
    }

    // ─── #5. Stale snapshot → snapshot_stale_vs_summary = true ───────────────────────
    [Test]
    public void RunInMemory_SnapshotAsOfDate20DaysBehindLatestBucket_StaleFlagSet()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 4, startDate: new DateOnly(2026, 2, 4));
        var staleAsOf = buckets[^1].PeriodEnd.AddDays(-20);
        var snapshot = MakeSnapshot(staleAsOf);
        var input = MakeOutput(MakeFund("LU0005", buckets, snapshot));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.That(m.DataQuality.SnapshotStaleVsSummary, Is.True);
    }

    // ─── #6. Edge case: <3 buckets → windows_total = bucket_count ───────────────────
    [Test]
    public void RunInMemory_TwoBucketsOnly_WindowsTotalIsTwo()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 2, startDate: new DateOnly(2026, 4, 1), return2wPct: 1m);
        var snapshot = MakeSnapshot(buckets[^1].PeriodEnd);
        var input = MakeOutput(MakeFund("LU0006", buckets, snapshot));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.WindowsTotal, Is.EqualTo(2));
            Assert.That(m.WindowsPositiveCount, Is.EqualTo(2));
        });
    }

    // ─── #7. Edge case: zero buckets and no snapshot → only total_fee_pct populated ──
    [Test]
    public void RunInMemory_ZeroBucketsAndNoSnapshot_OnlyTotalFeePopulated()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var input = MakeOutput(MakeFund("LU0007",
            buckets: Array.Empty<NavBucket>(), snapshot: null, totalFee: 1.25m));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.WindowsTotal, Is.EqualTo(0));
            Assert.That(m.WindowsPositiveCount, Is.EqualTo(0));
            Assert.That(m.CurrentDrawdownPct, Is.Null);
            Assert.That(m.AnnVolatility2wPct, Is.Null);
            Assert.That(m.Sharpe2w, Is.Null);
            Assert.That(m.Sharpe12w, Is.Null);
            Assert.That(m.Sharpe1y, Is.Null);
            Assert.That(m.NetReturnAfterFee12wPct, Is.Null);
            Assert.That(m.AsOfDate, Is.Null);
            Assert.That(m.TotalFeePct, Is.EqualTo(1.25m));
            Assert.That(m.DataQuality.SnapshotMissing, Is.True);
            Assert.That(m.DataQuality.Sharpe2wIsNan, Is.False);   // no latest bucket → flag false
        });
    }

    // ─── #8. Net-of-fee computation: 12% return − 2.38% × 12/52 ≈ 11.45% ─────────────
    [Test]
    public void RunInMemory_ComputesNetReturnAfterFee12w_FormulaCorrect()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = MakeBuckets(periods: 4, startDate: new DateOnly(2026, 2, 4));
        var snapshot = MakeSnapshot(buckets[^1].PeriodEnd, return12w: 26.26m);
        var input = MakeOutput(MakeFund("LU0008", buckets, snapshot, totalFee: 2.38m));

        var result = sut.RunInMemory(input, _config);

        var expected = 26.26m - 2.38m * 12m / 52m;
        Assert.That(result.Funds.Single().Metrics!.NetReturnAfterFee12wPct, Is.EqualTo(expected));
    }

    // ─── #9. Mixed bucket signs: positive count counts only > 0 ─────────────────────
    [Test]
    public void RunInMemory_MixedBucketSigns_PositiveCountReflectsLastThree()
    {
        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var buckets = new List<NavBucket>
        {
            MakeBucket(new DateOnly(2026, 1, 7),  new DateOnly(2026, 1, 20), return2wPct:  2.0m),
            MakeBucket(new DateOnly(2026, 1, 21), new DateOnly(2026, 2, 3),  return2wPct: -1.0m),
            MakeBucket(new DateOnly(2026, 2, 4),  new DateOnly(2026, 2, 17), return2wPct:  3.0m),
            MakeBucket(new DateOnly(2026, 2, 18), new DateOnly(2026, 3, 3),  return2wPct: -0.5m),
            MakeBucket(new DateOnly(2026, 3, 4),  new DateOnly(2026, 3, 17), return2wPct:  1.0m),
        };
        var snapshot = MakeSnapshot(buckets[^1].PeriodEnd);
        var input = MakeOutput(MakeFund("LU0009", buckets, snapshot));

        var result = sut.RunInMemory(input, _config);

        var m = result.Funds.Single().Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(m.WindowsTotal, Is.EqualTo(3));
            // last 3 returns: +3.0, -0.5, +1.0 → 2 positive
            Assert.That(m.WindowsPositiveCount, Is.EqualTo(2));
        });
    }

    // ─── #10. Real fixtures: deserialize 01 happy-path output → run step 02 → write ──
    [Test]
    public void Run_RealHappyPathFixture_ProducesEnrichedJsonFile()
    {
        const string runId = "test-happypath";
        // Sanity: step 01 output must already exist; the DataLoader test produces it.
        var step1Path = Paths.DataLoaderOutput("2026-W18", runId);
        if (!File.Exists(step1Path))
        {
            new FikaFinans.Infrastructure.Pipeline.Agents.DataLoaderAgent(new TestPathsService(), FikaFinans.InfrastructureV2.Tests.Storage.InMemoryPositionsRepository.SeededFromCsv(Paths.PositionsCsvAbs)).Run(
                "schroder", "2026-W18", runId);
        }

        var sut = _fixture.Create<MetricsCalculatorAgent>();
        var result = sut.Run("2026-W18", runId);

        var outPath = Paths.MetricsCalculatorOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True);

        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Has.Count.EqualTo(16));
            Assert.That(result.Funds.All(f => f.Metrics is not null), Is.True);
            Assert.That(result.Funds.All(f => f.Metrics!.TotalFeePct >= 0m), Is.True);
            Assert.That(result.Funds.All(f => f.Metrics!.WindowsTotal > 0), Is.True);
            Assert.That(result.Funds.All(f => f.Metrics!.AsOfDate is not null), Is.True);
            // Append-only: prior fields preserved.
            Assert.That(result.Funds[0].Metadata, Is.Not.Null);
            Assert.That(result.Funds[0].NavBuckets, Is.Not.Empty);
            Assert.That(result.Funds[0].Snapshot, Is.Not.Null);
        });
    }

    // ────────────────────────── helpers ──────────────────────────

    private static FundRecord MakeFund(
        string isin,
        IReadOnlyList<NavBucket> buckets,
        FundSnapshot? snapshot,
        decimal totalFee = 1.0m)
    {
        return new FundRecord
        {
            Isin           = isin,
            Metadata       = MakeMetadata(isin, totalFee),
            NavBuckets     = buckets,
            Snapshot       = snapshot,
            CurrentlyHeld  = false,
            CurrentValueKr = null,
            CostBasisKr    = null,
            Layer          = FundLayer.Active,
            Metrics        = null,
        };
    }

    private static FundMetadata MakeMetadata(string isin, decimal totalFee) => new()
    {
        Isin                     = isin,
        Name                     = $"Test Fund {isin}",
        CompanyName              = "TestCo",
        CurrencyCode             = "SEK",
        Category                 = "Test",
        FundType                 = "EQUITY_FUND",
        IsIndexFund              = false,
        ManagedType              = "ACTIVE",
        TotalFee                 = totalFee,
        ManagementFee            = totalFee * 0.7m,
        Risk                     = 4,
        Rating                   = 3,
        SharpeRatioStatic        = 1.0m,
        StandardDeviationStatic  = 12.0m,
        RecommendedHoldingPeriod = "FIVE_YEAR",
        Capital                  = 1_000_000m,
        NumberOfOwners           = 100,
    };

    private static List<NavBucket> MakeBuckets(int periods, DateOnly startDate, decimal return2wPct = 1.0m)
    {
        var list = new List<NavBucket>(periods);
        var cursor = startDate;
        for (var i = 0; i < periods; i++)
        {
            var end = cursor.AddDays(13);
            list.Add(MakeBucket(cursor, end, return2wPct));
            cursor = end.AddDays(1);
        }
        return list;
    }

    private static NavBucket MakeBucket(DateOnly start, DateOnly end, decimal return2wPct) => new()
    {
        PeriodStart        = start,
        PeriodEnd          = end,
        FirstNav           = 100m,
        LastNav            = 100m * (1 + return2wPct / 100m),
        NavHigh            = 105m,
        NavLow             = 98m,
        Return2wPct        = return2wPct,
        AnnVolatility2wPct = 12m,
        MaxDrawdown2wPct   = -2m,
        CurrentDrawdownPct = -1m,
        Sharpe2w           = 0.5m,
        BestDayPct         = 1m,
        WorstDayPct        = -1m,
        PctPositiveDays    = 50m,
        Skewness           = 0m,
    };

    private static FundSnapshot MakeSnapshot(
        DateOnly asOf,
        decimal? return12w = 12.0m,
        decimal? return1y = 22.0m,
        decimal? sharpe1y = 0.9m) => new()
    {
        AsOfDate              = asOf,
        Return12wCompoundPct  = return12w,
        AnnVolatility12wPct   = 14.0m,
        Sharpe12w             = 1.2m,
        MaxDrawdown12wPct     = -3.5m,
        Return1yCompoundPct   = return1y,
        AnnVolatility1yPct    = 16.0m,
        Sharpe1y              = sharpe1y,
        MaxDrawdown1yPct      = -8.0m,
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
}
