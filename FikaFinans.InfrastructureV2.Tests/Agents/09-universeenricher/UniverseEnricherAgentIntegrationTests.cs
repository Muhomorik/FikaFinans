using FikaFinans.Infrastructure.Pipeline.Llm.Foundry;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Application.Pipeline.Llm;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.InfrastructureV2.Tests.Agents.UniverseEnricher;

[TestFixture]
[Category("Integration")]
[TestOf(typeof(FoundryDifferentiatorLlmClient))]
public sealed class UniverseEnricherAgentIntegrationTests
{
    private const string EndpointKey   = "FOUNDRY_PROJECT_ENDPOINT";
    private const string ModelIdKey    = "FOUNDRY_DIFFERENTIATOR_MODEL_ID";
    private const string DefaultModelId = "gpt-5.4-1";

    private AIProjectClient _projectClient = null!;
    private string _modelId = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<UniverseEnricherAgentIntegrationTests>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live UniverseEnricher test. " +
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
    public async Task WriteDifferentiatorsAsync_RealFoundry_ReturnsNonEmptyDifferentiators()
    {
        // Arrange — two alternatives in the same category as the primary.
        var primary = MakeFund("LU0000000001", "Schroder ISF Global Energy A Acc SEK",
            "Branschfond, Energi", sharpe12w: 1.5m, totalFee: 1.5m);
        var altA = MakeFund("LU0000000002", "Storebrand Global Energi A",
            "Branschfond, Energi", sharpe12w: 1.2m, totalFee: 0.8m);
        var altB = MakeFund("LU0000000003", "BGF World Energy Fund A2",
            "Branschfond, Energi", sharpe12w: 0.9m, totalFee: 1.8m);

        var request = new DifferentiatorRequest
        {
            Primary      = primary,
            Alternatives = [altA, altB],
        };

        var sut = new FoundryDifferentiatorLlmClient(_projectClient, _modelId);

        // Act
        var result = await sut.WriteDifferentiatorsAsync(request);

        // Assert — LLM must return at least one non-empty differentiator with a valid ISIN.
        var validIsins = new HashSet<string> { "LU0000000002", "LU0000000003" };
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Empty);
            Assert.That(result.All(l => !string.IsNullOrWhiteSpace(l.Differentiator)), Is.True);
            Assert.That(result.All(l => validIsins.Contains(l.Isin.Value)), Is.True);
        });

        TestContext.Out.WriteLine($"Differentiators for {primary.Metadata.Name}:");
        foreach (var line in result)
            TestContext.Out.WriteLine($"  {line.Isin}: {line.Differentiator}");
    }

    [Test]
    public async Task RunInMemoryAsync_RealFoundry_EnrichesWithDifferentiators()
    {
        // Arrange — two Buy candidates in the same category so the LLM path fires
        // and at least one should get a non-empty differentiator.
        var buy1 = MakeRecommendedFund("LU0000000001", "Schroder ISF Global Energy A Acc SEK",
            "Branschfond, Energi", Recommendation.CatalystEntry, sharpe12w: 1.5m);
        var buy2 = MakeRecommendedFund("LU0000000002", "Storebrand Global Energi A",
            "Branschfond, Energi", Recommendation.MomentumEntry, sharpe12w: 1.2m);

        var input = MakeOutput("2026-W18", buy1, buy2);

        var llm = new FoundryDifferentiatorLlmClient(_projectClient, _modelId);
        var sut = new UniverseEnricherAgent(new TestPathsService(), llm);

        // Act
        var result = await sut.RunInMemoryAsync(input);

        // Assert
        var buys = result.Funds
            .Where(f => f.Recommendation == Recommendation.CatalystEntry
                     || f.Recommendation == Recommendation.MomentumEntry)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(buys, Is.Not.Empty);
            Assert.That(buys.All(f => f.ConvictionScore is not null), Is.True);
            Assert.That(buys.All(f => f.ConvictionBreakdown is not null), Is.True);
            Assert.That(
                buys.Any(f => f.Alternatives?.Any(a => !string.IsNullOrWhiteSpace(a.Differentiator)) == true),
                Is.True,
                "Expected at least one LLM-populated differentiator across the buy candidates");
        });

        // Persist for inspection.
        var outPath = Paths.UniverseEnricherOutput(input.IsoWeek, "integration");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(result, JsonOptions.Default));
        TestContext.Out.WriteLine($"UniverseEnricher output written to {outPath}");
    }

    #region Helpers

    private static FundRecord MakeFund(
        string isin,
        string name,
        string category,
        decimal sharpe12w,
        decimal totalFee) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin, name, category, totalFee),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Signal         = SignalLabel.Neutral,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        Metrics        = MakeMetrics(sharpe12w),
    };

    private static FundRecord MakeRecommendedFund(
        string isin,
        string name,
        string category,
        Recommendation recommendation,
        decimal sharpe12w) => new()
    {
        Isin           = isin,
        Metadata       = MakeMetadata(isin, name, category, totalFee: 1.2m),
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Signal         = SignalLabel.Strength,
        RuleFired      = "synthetic",
        CriteriaEvaluation = new CriteriaEvaluation { DataQualityWarnings = Array.Empty<string>() },
        MacroAlignment    = MacroAlignment.Partial,
        MatchedTheme      = null,
        PromotedToForming = false,
        PromotionReason   = null,
        Catalyst          = null,
        ThesisValidity    = ThesisValidity.Partial,
        ThesisRationale   = "Synthetic rationale.",
        ThesisMethod      = ThesisMethod.Matrix,
        Recommendation    = recommendation,
        RecommendationReason = $"Synthetic for {recommendation}.",
        Metrics           = MakeMetrics(sharpe12w),
    };

    private static FundMetadata MakeMetadata(
        string isin,
        string name,
        string category,
        decimal totalFee) => new()
    {
        Isin                     = isin,
        Name                     = name,
        CompanyName              = "Synthetic",
        CurrencyCode             = "SEK",
        Category                 = category,
        FundType                 = "EQUITY_FUND",
        IsIndexFund              = false,
        ManagedType              = "ACTIVE",
        TotalFee                 = totalFee,
        ManagementFee            = 0.7m,
        Risk                     = 5,
        Rating                   = 4,
        SharpeRatioStatic        = 0.8m,
        StandardDeviationStatic  = 0.15m,
        RecommendedHoldingPeriod = "FIVE_YEAR",
        Capital                  = 1_000_000m,
        NumberOfOwners           = 100,
    };

    private static Metrics MakeMetrics(decimal sharpe12w) => new()
    {
        WindowsPositiveCount = 2,
        WindowsTotal         = 3,
        CurrentDrawdownPct   = -1.0m,
        Sharpe2w             = 0.5m,
        Sharpe12w            = sharpe12w,
        Sharpe1y             = 1.0m,
        AnnVolatility12wPct  = 15m,
        AnnVolatility1yPct   = 17m,
        Return12wCompoundPct = 5m,
        Return1yCompoundPct  = 12m,
        MaxDrawdown12wPct    = -4m,
        MaxDrawdown1yPct     = -8m,
        DataQuality          = new MetricsDataQuality(),
    };

    private static DataLoaderOutput MakeOutput(string isoWeek, params FundRecord[] funds) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = isoWeek,
        Family          = "synthetic",
        RunId           = "integration",
        ConfigVersion   = "1.0.0",
        Funds           = funds,
        FrozenPositions = Array.Empty<FrozenPosition>(),
        CashAvailableKr = 0m,
        DataQuality     = new DataQuality(),
    };

    #endregion
}
