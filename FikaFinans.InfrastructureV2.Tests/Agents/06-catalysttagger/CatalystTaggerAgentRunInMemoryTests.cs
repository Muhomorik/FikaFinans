using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

/// <summary>
/// The domain-input <c>RunInMemoryAsync</c> — the call the per-fund step makes, step 5's
/// <c>IMacroAlignerAgent.RunInMemoryAsync</c> twin. The tagging rules themselves are covered
/// in <see cref="CatalystTaggerAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(CatalystTaggerAgent))]
public sealed class CatalystTaggerAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W18");
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;
    private Mock<IFundCatalystLlmClient> _llmMock = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);

        _llmMock = _fixture.Freeze<Mock<IFundCatalystLlmClient>>();
        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CatalystExposureClassification>());
    }

    private CatalystTaggerAgent CreateSut() => _fixture.Create<CatalystTaggerAgent>();

    [Test]
    public async Task RunInMemoryAsync_TwoFunds_TagsEachLikeProcessFundAsync()
    {
        // Arrange
        var sut = CreateSut();
        var hormuz = MakeCatalyst("Hormuz disruption", ["Branschfond, Energi"]);
        var energy = MakeFund("LU6001", "Branschfond, Energi");
        var credit = MakeFund("LU6002", "Räntefond, Företag");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                energy.Metadata.Name, It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Classify("Hormuz disruption", ExposureKind.Direct)]);

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, [energy, credit], [hormuz]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU6001", "LU6002" }));
            Assert.That(result.Funds[0].Catalyst!.Name, Is.EqualTo("Hormuz disruption"));
            Assert.That(result.Funds[0].Catalyst!.ExposureType, Is.EqualTo(ExposureType.Direct));
            Assert.That(result.Funds[1].Catalyst, Is.Null);
        });
    }

    [Test]
    public async Task RunInMemoryAsync_EveryFundWithCategory_CallsLlmOncePerFund()
    {
        // Arrange — unlike step 5 there is no code-only match, so a matching category still
        // goes to the LLM.
        var sut = CreateSut();
        var hormuz = MakeCatalyst("Hormuz disruption", ["Branschfond, Energi"]);

        // Act
        await sut.RunInMemoryAsync(Family, Week, RunId,
            [MakeFund("LU6003", "Branschfond, Energi"), MakeFund("LU6004", "Tillväxtmarknader")], [hormuz]);

        // Assert
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Catalyst>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task RunInMemoryAsync_CatalystWithNoAffectedCategories_NotSentToLlm()
    {
        // Arrange — the caller passes the context's list as stored; the agent filters it.
        var sut = CreateSut();
        var hormuz = MakeCatalyst("Hormuz disruption", ["Branschfond, Energi"]);
        var empty = MakeCatalyst("Empty catalyst", []);

        // Act
        await sut.RunInMemoryAsync(Family, Week, RunId, [MakeFund("LU6005", "Branschfond, Energi")], [hormuz, empty]);

        // Assert
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyList<Catalyst>>(list => list.Count == 1 && list[0].Name == "Hormuz disruption"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunInMemoryAsync_OnlyCatalystsWithNoAffectedCategories_CallsNoLlm()
    {
        // Arrange — filtered to nothing is the "no active catalysts" short-circuit.
        var sut = CreateSut();

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId,
            [MakeFund("LU6006", "Branschfond, Energi")], [MakeCatalyst("Empty catalyst", [])]);

        // Assert
        Assert.That(result.Funds.Single().Catalyst, Is.Null);
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Catalyst>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_TaggedFund_KeepsEveryEarlierField()
    {
        // Arrange — append-only: step 6 adds the catalyst, it never rewrites what came before.
        var sut = CreateSut();
        var hormuz = MakeCatalyst("Hormuz disruption", ["Branschfond, Energi"]);
        var fund = MakeFund("LU6007", "Branschfond, Energi");

        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Catalyst>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Classify("Hormuz disruption", ExposureKind.Direct)]);

        // Act
        var tagged = (await sut.RunInMemoryAsync(Family, Week, RunId, [fund], [hormuz])).Funds.Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(tagged.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(tagged.CriteriaEvaluation, Is.SameAs(fund.CriteriaEvaluation));
            Assert.That(tagged.MatchedTheme, Is.SameAs(fund.MatchedTheme));
            Assert.That(tagged.Signal, Is.EqualTo(fund.Signal));
            Assert.That(tagged.MacroAlignment, Is.EqualTo(fund.MacroAlignment));
            Assert.That(tagged.PromotedToForming, Is.EqualTo(fund.PromotedToForming));
            Assert.That(tagged.Layer, Is.EqualTo(fund.Layer));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_FundWarnings_LandInDataQualityWarnings()
    {
        // Arrange — an empty category is a per-fund warning, not a failure.
        var sut = CreateSut();
        var hormuz = MakeCatalyst("Hormuz disruption", ["Branschfond, Energi"]);

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, [MakeFund("LU6008", "")], [hormuz]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Single().Catalyst, Is.Null);
            Assert.That(result.DataQuality.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.DataQuality.Warnings[0], Does.Contain("LU6008"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_Always_StampsEnvelopeFromArguments()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.RunInMemoryAsync(
            Family, Week, RunId, Array.Empty<FundRecord>(), Array.Empty<Catalyst>());

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
    public void RunInMemoryAsync_NullFunds_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.RunInMemoryAsync(Family, Week, RunId, null!, Array.Empty<Catalyst>()));
    }

    [Test]
    public void RunInMemoryAsync_NullCatalysts_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.RunInMemoryAsync(Family, Week, RunId, Array.Empty<FundRecord>(), null!));
    }

    // ────────────────────────── helpers ──────────────────────────

    private static Catalyst MakeCatalyst(string name, IReadOnlyList<string> affected) => new()
    {
        Name               = name,
        Intensity          = Intensity.High,
        WeeksActive        = 8,
        AffectedCategories = affected,
        Rationale          = "synthetic",
    };

    private static CatalystExposureClassification Classify(string name, ExposureKind exposure) => new()
    {
        CatalystName = name,
        Exposure     = exposure,
        Rationale    = "synthetic",
    };

    private static FundRecord MakeFund(string isin, string category) => new()
    {
        Isin = isin,
        Metadata = new FundMetadata
        {
            Isin = isin,
            Name = $"Test Fund {isin}",
            CompanyName = "TestCo",
            CurrencyCode = "SEK",
            Category = category,
            FundType = "EQUITY_FUND",
            ManagedType = "ACTIVE",
            TotalFee = 1.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = Array.Empty<NavBucket>(),
        CurrentlyHeld = true,
        Layer = FundLayer.Active,
        Signal = SignalLabel.Weakness,
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
    };
}
