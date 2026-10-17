using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

/// <summary>
/// The domain-input <c>RunInMemoryAsync</c> — the call the per-fund step makes, step 4's
/// <c>ISignalScorerAgent.RunInMemory</c> twin. The matching rules themselves are covered
/// in <see cref="MacroAlignerAgentTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(MacroAlignerAgent))]
public sealed class MacroAlignerAgentRunInMemoryTests
{
    private static readonly Company Family = new("schroder");
    private static readonly IsoWeek Week = new("2026-W18");
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;
    private Mock<IThemeAdjacencyLlmClient> _llmMock = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // Strict: any path lookup throws, so every test here also proves the call reads
        // and writes no file.
        _fixture.Inject(new Mock<IPathsService>(MockBehavior.Strict).Object);

        _llmMock = _fixture.Freeze<Mock<IThemeAdjacencyLlmClient>>();
        _llmMock
            .Setup(x => x.ClassifyAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RotationTheme>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThemeAdjacencyVerdict.NoneVerdict);
    }

    private MacroAlignerAgent CreateSut() => _fixture.Create<MacroAlignerAgent>();

    [Test]
    public async Task RunInMemoryAsync_TwoFunds_AlignsEachLikeProcessFundAsync()
    {
        // Arrange
        var sut = CreateSut();
        var energy = MakeTheme("rot_theme_energy", SignalStrength.Strong, ["Branschfond, Energi"]);
        var matching = MakeFund("LU5001", "Branschfond, Energi");
        var other = MakeFund("LU5002", "Räntefond, Företag");

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, [matching, other], [energy]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU5001", "LU5002" }));
            Assert.That(result.Funds[0].MacroAlignment, Is.EqualTo(MacroAlignment.Strong));
            Assert.That(result.Funds[0].MatchedTheme!.Id, Is.EqualTo("rot_theme_energy"));
            Assert.That(result.Funds[1].MacroAlignment, Is.EqualTo(MacroAlignment.None));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_DirectMatch_CallsNoLlm()
    {
        // Arrange — the LLM is only for a fund whose category matches no theme directly.
        var sut = CreateSut();
        var energy = MakeTheme("rot_theme_energy", SignalStrength.Strong, ["Branschfond, Energi"]);

        // Act
        await sut.RunInMemoryAsync(Family, Week, RunId, [MakeFund("LU5003", "Branschfond, Energi")], [energy]);

        // Assert
        _llmMock.Verify(x => x.ClassifyAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RotationTheme>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunInMemoryAsync_AlignedFund_KeepsEveryEarlierField()
    {
        // Arrange — append-only: step 5 adds the alignment, it never rewrites what came before.
        var sut = CreateSut();
        var energy = MakeTheme("rot_theme_energy", SignalStrength.Moderate, ["Branschfond, Energi"]);
        var fund = MakeFund("LU5004", "Branschfond, Energi");

        // Act
        var aligned = (await sut.RunInMemoryAsync(Family, Week, RunId, [fund], [energy])).Funds.Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(aligned.Metadata, Is.SameAs(fund.Metadata));
            Assert.That(aligned.CriteriaEvaluation, Is.SameAs(fund.CriteriaEvaluation));
            Assert.That(aligned.Signal, Is.EqualTo(fund.Signal));
            Assert.That(aligned.RuleFired, Is.EqualTo(fund.RuleFired));
            Assert.That(aligned.Layer, Is.EqualTo(fund.Layer));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_FundWarnings_LandInDataQualityWarnings()
    {
        // Arrange — an empty category is a per-fund warning, not a failure.
        var sut = CreateSut();
        var energy = MakeTheme("rot_theme_energy", SignalStrength.Strong, ["Branschfond, Energi"]);

        // Act
        var result = await sut.RunInMemoryAsync(Family, Week, RunId, [MakeFund("LU5005", "")], [energy]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Single().MacroAlignment, Is.EqualTo(MacroAlignment.None));
            Assert.That(result.DataQuality.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.DataQuality.Warnings[0], Does.Contain("LU5005"));
        });
    }

    [Test]
    public async Task RunInMemoryAsync_Always_StampsEnvelopeFromArguments()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.RunInMemoryAsync(
            Family, Week, RunId, Array.Empty<FundRecord>(), Array.Empty<RotationTheme>());

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
            () => sut.RunInMemoryAsync(Family, Week, RunId, null!, Array.Empty<RotationTheme>()));
    }

    [Test]
    public void RunInMemoryAsync_NullThemes_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.RunInMemoryAsync(Family, Week, RunId, Array.Empty<FundRecord>(), null!));
    }

    // ────────────────────────── helpers ──────────────────────────

    private static RotationTheme MakeTheme(
        string id, SignalStrength strength, IReadOnlyList<string> affected) => new()
    {
        Id                 = id,
        Label              = $"Theme {id}",
        SignalStrength     = strength,
        AffectedCategories = affected,
        Rationale          = "synthetic",
        SourceChain        = null,
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
    };
}
