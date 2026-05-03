using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

[TestFixture]
[Category("Integration")]
public sealed class MacroAlignerAgentIntegrationTests
{
    private const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";
    private const string ModelIdKey  = "FOUNDRY_MACRO_ALIGN_MODEL_ID";
    // The adjacency check is a tiny single-shot classifier — a small/fast model
    // is appropriate. Default matches the cheap deployment used elsewhere; override
    // per environment via FOUNDRY_MACRO_ALIGN_MODEL_ID.
    private const string DefaultModelId = "gpt-5.4-1";

    private AIProjectClient _projectClient = null!;
    private string _modelId = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<MacroAlignerAgentIntegrationTests>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live MacroAligner test. " +
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
    public async Task RunInMemoryAsync_RealFoundry_ProducesValidAlignment()
    {
        // Arrange — synthetic step-04 + step-03 inputs.
        // One fund with a direct match (no LLM), one with an obscure category that
        // forces the LLM path.
        var directTheme = new RotationTheme
        {
            Id                 = "rot_theme_energy_2026-W18",
            Label              = "Integrated oil + inflation hedges",
            SignalStrength     = SignalStrength.Strong,
            AffectedCategories = ["Branschfond, Energi"],
            Rationale          = "synthetic",
            SourceChain        = null,
        };

        var asiaTheme = new RotationTheme
        {
            Id                 = "rot_theme_asia_2026-W18",
            Label              = "Asian domestic activity beneficiaries",
            SignalStrength     = SignalStrength.Moderate,
            AffectedCategories = ["Asien-fond"],
            Rationale          = "synthetic",
            SourceChain        = null,
        };

        var directFund = MakeFund("LU0000000001", "Branschfond, Energi", SignalLabel.Weakness);
        var llmFund    = MakeFund("LU0000000002", "Tillväxtmarknader",  SignalLabel.Strength);

        var input = new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = "2026-W18",
            Family          = "synthetic",
            RunId           = "macro-align-integration",
            ConfigVersion   = "1.0.0",
            Funds           = [directFund, llmFund],
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
            Catalysts        = Array.Empty<Catalyst>(),
            RotationThemes   = [directTheme, asiaTheme],
            Warnings         = null,
        };

        var llm = new FoundryThemeAdjacencyLlmClient(_projectClient, _modelId);
        var sut = new MacroAlignerAgent(llm);

        // Act
        var result = await sut.RunInMemoryAsync(input, macro);

        // Assert
        var direct = result.Funds.Single(f => f.Isin == "LU0000000001");
        var llmTagged = result.Funds.Single(f => f.Isin == "LU0000000002");

        Assert.Multiple(() =>
        {
            Assert.That(direct.MacroAlignment, Is.EqualTo(MacroAlignment.Strong));
            Assert.That(direct.MatchedTheme!.MatchMethod, Is.EqualTo(MatchMethod.DirectCategory));
            Assert.That(direct.MatchedTheme.Id, Is.EqualTo("rot_theme_energy_2026-W18"));

            // The LLM may return either Partial (if it sees Asia adjacency) or None
            // (if it's conservative). Both are valid contract outcomes — assert only
            // that the field is populated.
            Assert.That(llmTagged.MacroAlignment, Is.AnyOf(MacroAlignment.Partial, MacroAlignment.None));
            Assert.That(llmTagged.MatchedTheme, Is.Not.Null);
        });

        // Persist for inspection.
        var outPath = Paths.MacroAlignerOutput(input.IsoWeek, "integration");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath,
            JsonSerializer.Serialize(result, JsonOptions.Default));
        TestContext.Out.WriteLine($"MacroAligner output written to {outPath}");
    }

    private static FundRecord MakeFund(string isin, string category, SignalLabel signal) => new()
    {
        Isin           = isin,
        Metadata       = new FundMetadata
        {
            Isin                     = isin,
            Name                     = $"Synthetic {isin}",
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
        Signal         = signal,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
    };
}
