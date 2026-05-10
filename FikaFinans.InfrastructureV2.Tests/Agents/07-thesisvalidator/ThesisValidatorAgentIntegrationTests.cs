using FikaFinans.Infrastructure.Pipeline.Llm.Foundry;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Application.Pipeline.Configs;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.InfrastructureV2.Tests.Agents.ThesisValidator;

[TestFixture]
[Category("Integration")]
public sealed class ThesisValidatorAgentIntegrationTests
{
    private const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";
    private const string ModelIdKey  = "FOUNDRY_THESIS_VALIDATOR_MODEL_ID";
    // Single-fund refinement is a small/fast model task — same default as
    // CatalystTagger and MacroAligner.
    private const string DefaultModelId = "gpt-5.4-1";

    private AIProjectClient _projectClient = null!;
    private string _modelId = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<ThesisValidatorAgentIntegrationTests>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live ThesisValidator test. " +
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
    public async Task RunInMemoryAsync_RealFoundry_ProducesValidThesisLabels()
    {
        // Arrange — synthetic step-06 input. Three funds spanning the matrix:
        //   1. Energy + Weakness + Direct Hormuz catalyst → expect Invalid
        //   2. Tech + Strength + Direct AI catalyst + Strong macro → expect Valid
        //   3. Bond + Neutral, no catalyst → expect NotApplicable
        var hormuz = new FundCatalyst
        {
            Name         = "Hormuz disruption",
            Intensity    = Intensity.High,
            WeeksActive  = 8,
            ExposureType = ExposureType.Direct,
            Rationale    = "Energy fund directly benefits from Hormuz disruption.",
        };

        var ai = new FundCatalyst
        {
            Name         = "AI capex cycle",
            Intensity    = Intensity.Medium,
            WeeksActive  = 4,
            ExposureType = ExposureType.Direct,
            Rationale    = "Tech fund directly benefits from AI capex cycle.",
        };

        var energyFund = MakeFund("LU0000000001",
            "Branschfond, Energi", "Synthetic Energy",
            SignalLabel.Weakness, MacroAlignment.Strong, hormuz);
        var techFund = MakeFund("LU0000000002",
            "Branschfond, Teknik", "Synthetic Tech",
            SignalLabel.Strength, MacroAlignment.Strong, ai);
        var bondFund = MakeFund("LU0000000003",
            "Räntefond, Företag", "Synthetic Bond",
            SignalLabel.Neutral, MacroAlignment.None, null);

        var input = new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = "2026-W18",
            Family          = "synthetic",
            RunId           = "thesis-validator-integration",
            ConfigVersion   = "1.0.0",
            Funds           = [energyFund, techFund, bondFund],
            FrozenPositions = Array.Empty<FrozenPosition>(),
            CashAvailableKr = 0m,
            DataQuality     = new DataQuality(),
        };

        var llm = new FoundryThesisRefinementLlmClient(_projectClient, _modelId);
        var sut = new ThesisValidatorAgent(new TestPathsService(), llm);

        // Act
        var result = await sut.RunInMemoryAsync(input);

        // Assert
        var energy = result.Funds.Single(f => f.Isin == "LU0000000001");
        var tech   = result.Funds.Single(f => f.Isin == "LU0000000002");
        var bond   = result.Funds.Single(f => f.Isin == "LU0000000003");

        Assert.Multiple(() =>
        {
            // Weakness + Direct catalyst is the canonical Invalid case — LLM
            // must confirm. If it tries to jump to Valid we override to baseline,
            // so Invalid should be the verdict regardless.
            Assert.That(energy.ThesisValidity, Is.EqualTo(ThesisValidity.Invalid));
            Assert.That(energy.ThesisMethod, Is.AnyOf(ThesisMethod.Matrix, ThesisMethod.LlmRefinement));

            // Strength + catalyst + Strong macro is the canonical Valid case
            // and skips the LLM.
            Assert.That(tech.ThesisValidity, Is.EqualTo(ThesisValidity.Valid));
            Assert.That(tech.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));

            // Neutral always collapses to NotApplicable, no LLM.
            Assert.That(bond.ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
            Assert.That(bond.ThesisMethod, Is.EqualTo(ThesisMethod.Matrix));
        });

        // Persist for inspection.
        var outPath = Paths.ThesisValidatorOutput(input.IsoWeek, "integration");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath,
            JsonSerializer.Serialize(result, JsonOptions.Default));
        TestContext.Out.WriteLine($"ThesisValidator output written to {outPath}");
    }

    private static FundRecord MakeFund(
        string isin,
        string category,
        string name,
        SignalLabel signal,
        MacroAlignment macro,
        FundCatalyst? catalyst) => new()
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
        Metrics        = new Metrics
        {
            WindowsPositiveCount = 3,
            WindowsTotal         = 3,
            CurrentDrawdownPct   = 0m,
            AnnVolatility2wPct   = 12m,
            Sharpe2w             = 1.2m,
            Sharpe12w            = 1.5m,
            Sharpe1y             = 1.0m,
            AnnVolatility12wPct  = 14m,
            AnnVolatility1yPct   = 16m,
            Return12wCompoundPct = 6m,
            Return1yCompoundPct  = 12m,
            MaxDrawdown12wPct    = -4m,
            MaxDrawdown1yPct     = -8m,
            DataQuality          = new MetricsDataQuality(),
        },
        Signal         = signal,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = macro,
        MatchedTheme      = new MatchedTheme
        {
            Id          = "rot_theme_x",
            Label       = "Some theme",
            MatchMethod = macro == MacroAlignment.None ? MatchMethod.None : MatchMethod.DirectCategory,
        },
        PromotedToForming = false,
        PromotionReason   = null,
        Catalyst          = catalyst,
    };
}
