using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using Moq;
using DataLoaderJsonOptions = FikaFinans.InfrastructureV2.Tests.Models.DataLoader.JsonOptions;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

[TestFixture]
[TestOf(typeof(MacroAnalystAgent))]
public sealed class MacroAnalystAgentTests
{
    private const string Iso = "2026-W17";

    private IFixture _fixture = null!;
    private Mock<IMacroLlmClient> _llmMock = null!;
    private MacroAnalystAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _llmMock = _fixture.Freeze<Mock<IMacroLlmClient>>();
        _sut = _fixture.Create<MacroAnalystAgent>();
    }

    #region Validation

    [Test]
    public void ValidateInputs_StatusFailed_Throws()
    {
        // Arrange
        var summary = MakeSummary(status: RunStatus.Failed);
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("Failed"));
    }

    [Test]
    public void ValidateInputs_StatusPartial_AddsWarning()
    {
        // Arrange
        var summary = MakeSummary(status: RunStatus.Partial);
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        var warnings = new List<string>();

        // Act
        MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, warnings);

        // Assert
        Assert.That(warnings, Has.Some.Contains("Partial"));
    }

    [Test]
    public void ValidateInputs_PeriodMismatchAcrossAnalytics_Throws()
    {
        // Arrange
        var summary = MakeSummary(periodIsoWeek: "2026-W16");
        var chain = MakeChain(summary, periodIsoWeek: "2026-W17");
        var targets = MakeTargets(chain, periodIsoWeek: "2026-W17");
        var dl = MakeDataLoaderOutput("2026-W16", ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("mismatched periodIsoWeek"));
    }

    [Test]
    public void ValidateInputs_WeeklyToChainFkBroken_Throws()
    {
        // Arrange
        var summary = MakeSummary();
        var chainOrig = MakeChain(summary);
        var chain = new SubstitutionChainRun
        {
            ReportType         = chainOrig.ReportType,
            RunId              = chainOrig.RunId,
            Status             = chainOrig.Status,
            PeriodIsoWeek      = chainOrig.PeriodIsoWeek,
            WeeklySummaryRunId = "00000000-0000-0000-0000-000000000000",
            Chains             = chainOrig.Chains,
        };
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("FK chain broken"));
    }

    [Test]
    public void ValidateInputs_ChainToTargetsFkBroken_Throws()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targetsOrig = MakeTargets(chain);
        var targets = new OpportunityScanRun
        {
            ReportType             = targetsOrig.ReportType,
            RunId                  = targetsOrig.RunId,
            Status                 = targetsOrig.Status,
            PeriodIsoWeek          = targetsOrig.PeriodIsoWeek,
            SubstitutionChainRunId = "00000000-0000-0000-0000-000000000000",
            Targets                = targetsOrig.Targets,
        };
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("FK chain broken"));
    }

    [Test]
    public void ValidateInputs_BundleDriftVsDataLoader_Throws()
    {
        // Arrange
        var summary = MakeSummary(periodIsoWeek: "2026-W17");
        var chain = MakeChain(summary, periodIsoWeek: "2026-W17");
        var targets = MakeTargets(chain, periodIsoWeek: "2026-W17");
        var dl = MakeDataLoaderOutput("2026-W18", ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("bundle drift"));
    }

    #endregion

    #region Slugify

    [Test]
    public void Slugify_MixedPunctuation_LowercasesAndJoinsWithUnderscores()
    {
        // Arrange
        var label = "Capital flows into AI/Semis!";

        // Act
        var slug = MacroAnalystAgent.Slugify(label);

        // Assert
        Assert.That(slug, Is.EqualTo("capital_flows_into_ai_semis"));
    }

    [Test]
    public void Slugify_Empty_ReturnsUntitled()
    {
        // Arrange
        var label = "";

        // Act
        var slug = MacroAnalystAgent.Slugify(label);

        // Assert
        Assert.That(slug, Is.EqualTo("untitled"));
    }

    #endregion

    #region ExtractCategories

    [Test]
    public void ExtractCategories_DistinctSorted_ReturnsUnique()
    {
        // Arrange
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi", "Globalfond", "Branschfond, Energi"]);

        // Act
        var cats = MacroAnalystAgent.ExtractCategories(dl);

        // Assert
        Assert.That(cats, Is.EquivalentTo(new[] { "Branschfond, Energi", "Globalfond" }));
    }

    #endregion

    #region RunInMemoryAsync — happy path

    [Test]
    public async Task RunInMemoryAsync_HappyPath_ProducesExpectedContext()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi", "Globalfond"]);
        SetupLlm("""
        {
          "macro_regime": "Mixed",
          "macro_regime_secondary": null,
          "regime_confidence": 0.62,
          "catalysts": [
            {
              "name": "Hormuz tensions",
              "intensity": "high",
              "weeks_active": 3,
              "affected_categories": ["Branschfond, Energi"],
              "rationale": "Crude rallying on geopolitical risk."
            }
          ],
          "rotation_themes": [
            {
              "label": "Capital flows into Energy",
              "signal_strength": "Moderate",
              "affected_categories": ["Branschfond, Energi"],
              "rationale": "One chain points there.",
              "source_chain": {
                "capital_fleeing": "Cyclicals",
                "flows_toward": "Energy"
              }
            }
          ]
        }
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Iso);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ctx.IsoWeek, Is.EqualTo(Iso));
            Assert.That(ctx.MacroRegime, Is.EqualTo(MacroRegime.Mixed));
            Assert.That(ctx.RegimeConfidence, Is.EqualTo(0.62m));
            Assert.That(ctx.NetMoodInput, Is.EqualTo(MarketSentiment.Mixed));
            Assert.That(ctx.SourceRunIds.WeeklySummaryRunId, Is.EqualTo(summary.RunId));
            Assert.That(ctx.SourceRunIds.SubstitutionChainRunId, Is.EqualTo(chain.RunId));
            Assert.That(ctx.SourceRunIds.RotationTargetsRunId, Is.EqualTo(targets.RunId));
            Assert.That(ctx.Catalysts, Has.Count.EqualTo(1));
            Assert.That(ctx.Catalysts[0].Intensity, Is.EqualTo(Intensity.High));
            Assert.That(ctx.RotationThemes, Has.Count.EqualTo(1));
            Assert.That(ctx.RotationThemes[0].Id, Does.StartWith("rot_theme_capital_flows_into_energy_"));
            Assert.That(ctx.RotationThemes[0].SignalStrength, Is.EqualTo(SignalStrength.Moderate));
        });
        _llmMock.Verify(
            x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region RunInMemoryAsync — universe filtering

    [Test]
    public async Task RunInMemoryAsync_HallucinatedCategory_FilteredOut()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("""
        {
          "macro_regime": "RiskOff",
          "regime_confidence": 0.5,
          "catalysts": [
            {
              "name": "Made-up event",
              "intensity": "low",
              "weeks_active": 1,
              "affected_categories": ["AI Hardware", "NotARealCategory"],
              "rationale": "..."
            }
          ],
          "rotation_themes": []
        }
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Iso);

        // Assert
        Assert.That(ctx.Catalysts, Is.Empty);
        Assert.That(ctx.Warnings, Is.Not.Null);
        Assert.That(ctx.Warnings!, Has.Some.Contains("dropping catalyst"));
    }

    #endregion

    #region RunInMemoryAsync — retry behavior

    [Test]
    public async Task RunInMemoryAsync_RegimeConfidenceOutOfRange_RetriesAndUsesValid()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        var bad = """
        {
          "macro_regime": "Mixed",
          "regime_confidence": 1.7,
          "catalysts": [],
          "rotation_themes": []
        }
        """;
        var good = """
        {
          "macro_regime": "Mixed",
          "regime_confidence": 0.4,
          "catalysts": [],
          "rotation_themes": []
        }
        """;
        _llmMock.SetupSequence(x =>
                x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bad)
            .ReturnsAsync(good);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Iso);

        // Assert
        Assert.That(ctx.RegimeConfidence, Is.EqualTo(0.4m));
        _llmMock.Verify(
            x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public void RunInMemoryAsync_RetryAlsoFails_Throws()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("{ this is not json at all");

        // Act + Assert
        Assert.ThrowsAsync<MacroAnalystValidationException>(
            async () => await _sut.RunInMemoryAsync(summary, chain, targets, dl, Iso));
    }

    [Test]
    public async Task RunInMemoryAsync_ExtractsJsonFromMarkdownFence()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("""
        Here is the analysis:
        ```json
        {
          "macro_regime": "Stagflation",
          "regime_confidence": 0.55,
          "catalysts": [],
          "rotation_themes": []
        }
        ```
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Iso);

        // Assert
        Assert.That(ctx.MacroRegime, Is.EqualTo(MacroRegime.Stagflation));
    }

    #endregion

    #region Round-trip serialization

    [Test]
    public async Task RunInMemoryAsync_RoundTripsThroughJsonOptions()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("""
        {
          "macro_regime": "RiskOn",
          "regime_confidence": 0.8,
          "catalysts": [{
            "name": "Earnings tailwind",
            "intensity": "medium",
            "weeks_active": 2,
            "affected_categories": ["Branschfond, Energi"],
            "rationale": "Beats."
          }],
          "rotation_themes": []
        }
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Iso);
        var json = JsonSerializer.Serialize(ctx, DataLoaderJsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<MacroContext>(json, DataLoaderJsonOptions.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"macro_regime\": \"RiskOn\""));
            Assert.That(json, Does.Contain("\"net_mood_input\": \"Mixed\""));
            Assert.That(json, Does.Contain("\"intensity\": \"medium\""));
            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.MacroRegime, Is.EqualTo(MacroRegime.RiskOn));
            Assert.That(roundTripped.Catalysts[0].Intensity, Is.EqualTo(Intensity.Medium));
        });
    }

    #endregion

    #region Helpers

    private void SetupLlm(string llmResponse) =>
        _llmMock.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

    private static WeeklySummaryRun MakeSummary(
        string? runId = null,
        RunStatus status = RunStatus.Success,
        string periodIsoWeek = Iso) =>
        new()
        {
            ReportType    = "weekly-summary",
            RunId         = runId ?? Guid.NewGuid().ToString(),
            Status        = status,
            PeriodIsoWeek = periodIsoWeek,
            NetMood       = MarketSentiment.Mixed,
            MoodSummary   = "Mixed week.",
            Themes        = new List<WeeklySummaryTheme>
            {
                new() {
                    Category   = "Energy",
                    Summary    = "Crude up.",
                    Confidence = ConfidenceLevel.High,
                    Sentiment  = MarketSentiment.RiskOff,
                },
            },
        };

    private static SubstitutionChainRun MakeChain(
        WeeklySummaryRun summary,
        string? runId = null,
        string? periodIsoWeek = null,
        RunStatus status = RunStatus.Success) =>
        new()
        {
            ReportType         = "substitution-chain",
            RunId              = runId ?? Guid.NewGuid().ToString(),
            Status             = status,
            PeriodIsoWeek      = periodIsoWeek ?? summary.PeriodIsoWeek,
            WeeklySummaryRunId = summary.RunId,
            Chains             = new List<RotationChain>
            {
                new() { CapitalFleeing = "Cyclicals", FlowsToward = "Energy", Mechanism = "Oil pressure" },
            },
        };

    private static OpportunityScanRun MakeTargets(
        SubstitutionChainRun chain,
        string? runId = null,
        string? periodIsoWeek = null,
        RunStatus status = RunStatus.Success) =>
        new()
        {
            ReportType             = "rotation-targets",
            RunId                  = runId ?? Guid.NewGuid().ToString(),
            Status                 = status,
            PeriodIsoWeek          = periodIsoWeek ?? chain.PeriodIsoWeek,
            SubstitutionChainRunId = chain.RunId,
            Targets                = new List<RotationTarget>
            {
                new() {
                    Category       = "Energy",
                    SignalStrength = SignalStrength.Moderate,
                    Rationale      = "One chain.",
                    RiskCaveat     = "De-escalation.",
                },
            },
        };

    private static DataLoaderOutput MakeDataLoaderOutput(string isoWeek, IReadOnlyList<string> categories)
    {
        var fund = (string cat, int i) => new FundRecord
        {
            Isin = $"LU{i:D10}",
            Metadata = new FundMetadata
            {
                Isin                     = $"LU{i:D10}",
                Name                     = $"Fund {i}",
                CompanyName              = "Schroder",
                CurrencyCode             = "SEK",
                Category                 = cat,
                FundType                 = "Equity",
                IsIndexFund              = false,
                ManagedType              = "Active",
                TotalFee                 = 1.5m,
                ManagementFee            = 1.0m,
                Risk                     = 5,
                Rating                   = 4,
                SharpeRatioStatic        = 0.5m,
                StandardDeviationStatic  = 0.15m,
                RecommendedHoldingPeriod = "5 years",
                Capital                  = 1_000_000m,
                NumberOfOwners           = 100,
            },
            NavBuckets    = new List<NavBucket>(),
            Snapshot      = null,
            CurrentlyHeld = false,
            Layer         = FundLayer.Active,
        };
        var funds = categories.Select((c, i) => fund(c, i)).ToList();
        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = isoWeek,
            Family          = "schroder",
            RunId           = "test-run",
            ConfigVersion   = "1.0.0",
            Funds           = funds,
            FrozenPositions = new List<FrozenPosition>(),
            CashAvailableKr = 0m,
            DataQuality     = new DataQuality(),
        };
    }

    #endregion
}
