using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.Recommender;

/// <summary>
/// The domain-input <c>RunInMemory</c> — the call the per-fund step makes, step 4's
/// <c>ISignalScorerAgent.RunInMemory</c> twin. The mapping table itself is covered in
/// <see cref="RecommenderAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(RecommenderAgent))]
public sealed class RecommenderAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W18");
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);
    }

    private RecommenderAgent CreateSut() => _fixture.Create<RecommenderAgent>();

    [Test]
    public void RunInMemory_TwoFunds_RecommendsEachLikeProcessFund()
    {
        // Arrange
        var sut = CreateSut();
        var exit = MakeFund("LU8001", SignalLabel.Weakness, ThesisValidity.Invalid, MakeCatalyst(), held: true);
        var skip = MakeFund("LU8002", SignalLabel.Neutral, ThesisValidity.NotApplicable, null, held: false);

        // Act
        var result = sut.RunInMemory(Family, Week, RunId, [exit, skip]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU8001", "LU8002" }));
            Assert.That(result.Funds[0].Recommendation, Is.EqualTo(Recommendation.ThesisExit));
            Assert.That(result.Funds[1].Recommendation, Is.EqualTo(Recommendation.Skip));
            Assert.That(result.Funds.Select(f => f.RecommendationReason), Has.All.Not.Empty);
        });
    }

    [Test]
    public void RunInMemory_RecommendedFund_KeepsEveryEarlierField()
    {
        // Arrange — append-only: step 8 adds the recommendation, it never rewrites what came before.
        var sut = CreateSut();
        var fund = MakeFund("LU8003", SignalLabel.Strength, ThesisValidity.Valid, MakeCatalyst(), held: false);

        // Act
        var recommended = sut.RunInMemory(Family, Week, RunId, [fund]).Funds.Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(recommended.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(recommended.CriteriaEvaluation, Is.SameAs(fund.CriteriaEvaluation));
            Assert.That(recommended.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(recommended.Catalyst, Is.SameAs(fund.Catalyst));
            Assert.That(recommended.Signal, Is.EqualTo(fund.Signal));
            Assert.That(recommended.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(recommended.ThesisValidity, Is.EqualTo(fund.ThesisValidity));
            Assert.That(recommended.ThesisRationale, Is.EqualTo(fund.ThesisRationale));
            Assert.That(recommended.ThesisMethod, Is.EqualTo(fund.ThesisMethod));
            Assert.That(recommended.CurrentlyHeld, Is.EqualTo(fund.CurrentlyHeld));
        });
    }

    [Test]
    public void RunInMemory_FundWarnings_LandInDataQualityWarnings()
    {
        // Arrange — a null signal is a per-fund warning, not a failure.
        var sut = CreateSut();

        // Act
        var result = sut.RunInMemory(Family, Week, RunId,
            [MakeFund("LU8004", null, null, null, held: true)]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Single().Recommendation, Is.EqualTo(Recommendation.Skip));
            Assert.That(result.DataQuality.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.DataQuality.Warnings[0], Does.Contain("LU8004"));
        });
    }

    [Test]
    public void RunInMemory_Always_StampsEnvelopeFromArguments()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.RunInMemory(Family, Week, RunId, Array.Empty<FundRecord>());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Family, Is.EqualTo("schroder"));
            Assert.That(result.IsoWeek, Is.EqualTo("2026-W18"));
            Assert.That(result.RunId, Is.EqualTo(RunId));
            Assert.That(result.Funds, Is.Empty);
            Assert.That(result.DataQuality.Warnings, Is.Empty);
        });
    }

    [Test]
    public void RunInMemory_Always_LeavesPortfolioFieldsEmpty()
    {
        // Arrange — no rule reads them; step 10 is their only reader.
        var sut = CreateSut();

        // Act
        var result = sut.RunInMemory(Family, Week, RunId,
            [MakeFund("LU8005", SignalLabel.Forming, ThesisValidity.Partial, null, held: true)]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.FrozenPositions, Is.Empty);
            Assert.That(result.CashAvailableKr, Is.Zero);
        });
    }

    [Test]
    public void RunInMemory_NullFunds_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.RunInMemory(Family, Week, RunId, null!));
    }

    // ────────────────────────── helpers ──────────────────────────

    private static FundCatalyst MakeCatalyst() => new()
    {
        Name         = "Hormuz disruption",
        Intensity    = Intensity.High,
        WeeksActive  = 8,
        ExposureType = ExposureType.Direct,
        Rationale    = "synthetic",
    };

    private static FundRecord MakeFund(
        string isin, SignalLabel? signal, ThesisValidity? thesis, FundCatalyst? catalyst, bool held) => new()
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
        Signal = signal,
        RuleFired = "synthetic_rule",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment = MacroAlignment.Strong,
        MatchedTheme = new MatchedTheme
        {
            Id          = "rot_theme_energy",
            Label       = "Integrated oil + inflation hedges",
            MatchMethod = MatchMethod.DirectCategory,
        },
        PromotedToForming = false,
        Catalyst = catalyst,
        ThesisValidity = thesis,
        ThesisRationale = thesis is null ? null : "Upstream rationale.",
        ThesisMethod = thesis is null ? null : ThesisMethod.Matrix,
    };
}
