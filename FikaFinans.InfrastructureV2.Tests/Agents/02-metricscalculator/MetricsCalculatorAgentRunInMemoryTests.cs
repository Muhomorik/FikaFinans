using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MetricsCalculator;

/// <summary>
/// The domain-input <c>RunInMemory</c> — the call the per-fund step makes, step 1's
/// <c>IDataLoaderAgent.RunInMemory</c> twin. The metric rules themselves are covered in
/// <see cref="MetricsCalculatorAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(MetricsCalculatorAgent))]
public sealed class MetricsCalculatorAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W18");
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;
    private MetricsCalculatorConfig _config = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);

        _config = new MetricsCalculatorConfig();
    }

    private MetricsCalculatorAgent CreateSut() => _fixture.Create<MetricsCalculatorAgent>();

    [Test]
    public void RunInMemory_TwoFunds_EnrichesEachLikeProcessFund()
    {
        // Arrange
        var sut = CreateSut();
        var buckets = MakeBuckets(periods: 6, startDate: new DateOnly(2026, 1, 7));
        var withSnapshot = MakeFund("LU2001", buckets, MakeSnapshot(buckets[^1].PeriodEnd));
        var withoutSnapshot = MakeFund("LU2002", buckets, snapshot: null);

        // Act
        var result = sut.RunInMemory(Family, Week, RunId, [withSnapshot, withoutSnapshot], _config);

        // Assert
        var expected = sut.ProcessFund(withSnapshot, _config).Metrics!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU2001", "LU2002" }));
            Assert.That(result.Funds[0].Metrics!.NetReturnAfterFee12wPct, Is.EqualTo(expected.NetReturnAfterFee12wPct));
            Assert.That(result.Funds[0].Metrics!.WindowsPositiveCount, Is.EqualTo(expected.WindowsPositiveCount));
            Assert.That(result.Funds[1].Metrics!.DataQuality.SnapshotMissing, Is.True);
        });
    }

    [Test]
    public void RunInMemory_Always_StampsEnvelopeFromArguments()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.RunInMemory(Family, Week, RunId, Array.Empty<FundRecord>(), _config);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Family, Is.EqualTo("schroder"));
            Assert.That(result.IsoWeek, Is.EqualTo("2026-W18"));
            Assert.That(result.RunId, Is.EqualTo(RunId));
            Assert.That(result.Funds, Is.Empty);
        });
    }

    [Test]
    public void RunInMemory_NullFunds_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.RunInMemory(Family, Week, RunId, null!, _config));
    }

    [Test]
    public void RunInMemory_NullConfig_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => sut.RunInMemory(Family, Week, RunId, Array.Empty<FundRecord>(), null!));
    }

    // ────────────────────────── helpers ──────────────────────────

    private static FundRecord MakeFund(string isin, IReadOnlyList<NavBucket> buckets, FundSnapshot? snapshot) => new()
    {
        Isin = isin,
        Metadata = new FundMetadata
        {
            Isin = isin,
            Name = $"Test Fund {isin}",
            CompanyName = "TestCo",
            CurrencyCode = "SEK",
            Category = "Test",
            FundType = "EQUITY_FUND",
            ManagedType = "ACTIVE",
            TotalFee = 2.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = buckets,
        Snapshot = snapshot,
        Layer = FundLayer.Active,
    };

    private static List<NavBucket> MakeBuckets(int periods, DateOnly startDate)
    {
        var list = new List<NavBucket>(periods);
        var cursor = startDate;
        for (var i = 0; i < periods; i++)
        {
            var end = cursor.AddDays(13);
            list.Add(new NavBucket
            {
                PeriodStart = cursor,
                PeriodEnd = end,
                FirstNav = 100m,
                LastNav = 101.5m,
                NavHigh = 105m,
                NavLow = 98m,
                Return2wPct = 1.5m,
                AnnVolatility2wPct = 12m,
                MaxDrawdown2wPct = -2m,
                CurrentDrawdownPct = -1m,
                Sharpe2w = 0.5m,
                BestDayPct = 1m,
                WorstDayPct = -1m,
                PctPositiveDays = 50m,
                Skewness = 0m,
            });
            cursor = end.AddDays(1);
        }
        return list;
    }

    private static FundSnapshot MakeSnapshot(DateOnly asOf) => new()
    {
        AsOfDate = asOf,
        Return12wCompoundPct = 12.0m,
        AnnVolatility12wPct = 14.0m,
        Sharpe12w = 1.2m,
        MaxDrawdown12wPct = -3.5m,
        Return1yCompoundPct = 22.0m,
        AnnVolatility1yPct = 16.0m,
        Sharpe1y = 0.9m,
        MaxDrawdown1yPct = -8.0m,
    };
}
