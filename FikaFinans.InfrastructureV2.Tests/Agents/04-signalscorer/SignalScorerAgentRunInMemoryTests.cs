using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.SignalScorer;

/// <summary>
/// The domain-input <c>RunInMemory</c> — the call the per-fund step makes, step 2's
/// <c>IMetricsCalculatorAgent.RunInMemory</c> twin. The scoring rules themselves are covered
/// in <see cref="SignalScorerAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(SignalScorerAgent))]
public sealed class SignalScorerAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W18");
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;
    private SignalScorerConfig _config = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);

        _config = SignalScorerConfig.Default;
    }

    private SignalScorerAgent CreateSut() => _fixture.Create<SignalScorerAgent>();

    [Test]
    public void RunInMemory_TwoFunds_ScoresEachLikeProcessFund()
    {
        // Arrange
        var sut = CreateSut();
        var strong = MakeFund("LU4001", MakeMetrics(windowsPositive: 3, currentDd: 0m, sharpe12w: 1.5m, sharpe2w: 1.0m));
        var weak = MakeFund("LU4002", MakeMetrics(windowsPositive: 2, currentDd: -5.5m, sharpe12w: 0.2m, sharpe2w: -3.5m));

        // Act
        var result = sut.RunInMemory(Family, Week, RunId, [strong, weak], _config);

        // Assert
        var expectedWeak = sut.ProcessFund(weak, _config);
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU4001", "LU4002" }));
            Assert.That(result.Funds[0].Signal, Is.EqualTo(SignalLabel.Strength));
            Assert.That(result.Funds[1].Signal, Is.EqualTo(expectedWeak.Signal));
            Assert.That(result.Funds[1].RuleFired, Is.EqualTo(expectedWeak.RuleFired));
        });
    }

    [Test]
    public void RunInMemory_ScoredFund_KeepsEveryEarlierField()
    {
        // Arrange — append-only: step 4 adds the signal, it never rewrites what came before.
        var sut = CreateSut();
        var fund = MakeFund("LU4003", MakeMetrics(windowsPositive: 3, currentDd: 0m, sharpe12w: 1.5m, sharpe2w: 1.0m));

        // Act
        var scored = sut.RunInMemory(Family, Week, RunId, [fund], _config).Funds.Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(scored.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(scored.Metrics, Is.SameAs(fund.Metrics));
            Assert.That(scored.Layer, Is.EqualTo(fund.Layer));
            Assert.That(scored.CurrentlyHeld, Is.EqualTo(fund.CurrentlyHeld));
            Assert.That(scored.CriteriaEvaluation, Is.Not.Null);
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

    private static FundRecord MakeFund(string isin, Metrics metrics) => new()
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
            TotalFee = 1.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = Array.Empty<NavBucket>(),
        CurrentlyHeld = true,
        Layer = FundLayer.Active,
        Metrics = metrics,
    };

    private static Metrics MakeMetrics(int windowsPositive, decimal currentDd, decimal sharpe12w, decimal sharpe2w) => new()
    {
        WindowsPositiveCount = windowsPositive,
        WindowsTotal = 3,
        CurrentDrawdownPct = currentDd,
        AnnVolatility2wPct = 12m,
        Sharpe2w = sharpe2w,
        Sharpe12w = sharpe12w,
        AsOfDate = new DateOnly(2026, 4, 28),
        DataQuality = new MetricsDataQuality { BucketsUsed = 26 },
    };
}
