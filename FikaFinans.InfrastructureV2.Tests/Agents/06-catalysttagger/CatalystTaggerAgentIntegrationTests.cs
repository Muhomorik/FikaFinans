using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.InfrastructureV2.Tests.Models.CatalystTagger;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;
using FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

[TestFixture]
[Category("Integration")]
public sealed class CatalystTaggerAgentIntegrationTests
{
    private const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";
    private const string ModelIdKey  = "FOUNDRY_CATALYST_TAGGER_MODEL_ID";
    // Catalyst tagging is a per-fund classification — small/fast model is fine.
    private const string DefaultModelId = "gpt-5.4-1";

    private AIProjectClient _projectClient = null!;
    private string _modelId = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<CatalystTaggerAgentIntegrationTests>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live CatalystTagger test. " +
                $"Run: dotnet user-secrets set {EndpointKey} https://<your-foundry-project> " +
                $"--project FikaFinans.InfrastructureV2.Tests");
        }

        _modelId = config[ModelIdKey] ?? DefaultModelId;

        var clientOptions = new AIProjectClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(2),
        };
        _projectClient = new AIProjectClient(new Uri(endpoint!), new DefaultAzureCredential(), clientOptions);
    }

    [Test]
    public async Task RunInMemoryAsync_RealFoundry_ProducesValidCatalystTags()
    {
        // Arrange — synthetic step-05 + step-03 inputs.
        // Two funds: one with a clear Direct match (energy + Hormuz), one that
        // should resolve to None (a bond fund + tech catalyst).
        var hormuz = new Catalyst
        {
            Name               = "Hormuz disruption",
            Intensity          = Intensity.High,
            WeeksActive        = 8,
            AffectedCategories = ["Branschfond, Energi", "Energy"],
            Rationale          = "synthetic",
        };

        var ai = new Catalyst
        {
            Name               = "AI capex cycle",
            Intensity          = Intensity.Medium,
            WeeksActive        = 4,
            AffectedCategories = ["Branschfond, Teknik"],
            Rationale          = "synthetic",
        };

        var energyFund = MakeFund("LU0000000001", "Branschfond, Energi", "Synthetic Energy");
        var bondFund   = MakeFund("LU0000000002", "Räntefond, Företag",  "Synthetic Bond");

        var input = new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = "2026-W18",
            Family          = "synthetic",
            RunId           = "catalyst-tagger-integration",
            ConfigVersion   = "1.0.0",
            Funds           = [energyFund, bondFund],
            FrozenPositions = Array.Empty<FrozenPosition>(),
            CashAvailableKr = 0m,
            DataQuality     = new DataQuality(),
        };

        var macro = new MacroContext
        {
            GeneratedAt      = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek          = "2026-W18",
            ConfigVersion    = "1.0.0",
            SourceRunIds     = new SourceRunIds
            {
                WeeklySummaryRunId      = "ws-int",
                SubstitutionChainRunId  = "sc-int",
                RotationTargetsRunId    = "rt-int",
            },
            MacroRegime      = MacroRegime.Mixed,
            RegimeConfidence = 0.7m,
            NetMoodInput     = MarketSentiment.Mixed,
            Catalysts        = [hormuz, ai],
            RotationThemes   = Array.Empty<RotationTheme>(),
            Warnings         = null,
        };

        var llm = new FoundryFundCatalystLlmClient(_projectClient, _modelId);
        var sut = new CatalystTaggerAgent(llm);

        // Act
        var result = await sut.RunInMemoryAsync(input, macro);

        // Assert
        var energy = result.Funds.Single(f => f.Isin == "LU0000000001");
        var bond   = result.Funds.Single(f => f.Isin == "LU0000000002");

        Assert.Multiple(() =>
        {
            // The energy fund + Hormuz catalyst is the cleanest direct match the
            // taxonomy allows — assert it gets tagged Direct on Hormuz.
            Assert.That(energy.Catalyst, Is.Not.Null,
                "Energy fund should be tagged with the Hormuz catalyst.");
            Assert.That(energy.Catalyst!.Name, Is.EqualTo("Hormuz disruption"));
            Assert.That(energy.Catalyst.ExposureType, Is.EqualTo(ExposureType.Direct));
            Assert.That(energy.Catalyst.Intensity, Is.EqualTo(Intensity.High));
            Assert.That(energy.Catalyst.WeeksActive, Is.EqualTo(8));

            // The bond fund could plausibly come back as null or Indirect — both are
            // valid contract outcomes. Assert only that the model didn't hallucinate
            // a Direct tag on something far from the affected categories.
            Assert.That(bond.Catalyst?.ExposureType, Is.Not.EqualTo(ExposureType.Direct),
                "Bond fund should not be Direct on either Energy or Tech catalysts.");
        });

        // Persist for inspection.
        var outPath = Paths.CatalystTaggerOutput(input.IsoWeek, "integration");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath,
            JsonSerializer.Serialize(result, JsonOptions.Default));
        TestContext.Out.WriteLine($"CatalystTagger output written to {outPath}");
    }

    private static FundRecord MakeFund(string isin, string category, string name) => new()
    {
        Isin           = isin,
        Metadata       = new FundMetadata
        {
            Isin                     = isin,
            Name                     = name,
            CompanyName              = "Synthetic",
            CurrencyCode             = "SEK",
            Category                 = category,
            FundType                 = "EQUITY_FUND",
            IsIndexFund              = false,
            ManagedType              = "ACTIVE",
            TotalFee                 = 1.0m,
            ManagementFee            = 0.7m,
            Risk                     = 5,
            Rating                   = 4,
            SharpeRatioStatic        = 0.5m,
            StandardDeviationStatic  = 0.15m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
            Capital                  = 1_000_000m,
            NumberOfOwners           = 100,
        },
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Signal         = SignalLabel.Strength,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = MacroAlignment.None,
        MatchedTheme      = MatchedTheme.None,
        PromotedToForming = false,
        PromotionReason   = null,
    };
}
