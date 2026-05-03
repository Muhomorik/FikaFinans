using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using Microsoft.Extensions.Configuration;
using DataLoaderJsonOptions = FikaFinans.InfrastructureV2.Tests.Models.DataLoader.JsonOptions;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

[TestFixture]
[Category("Integration")]
public sealed class MacroAnalystAgentIntegrationTests
{
    private const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";
    private const string ModelIdKey  = "FOUNDRY_MACRO_MODEL_ID";
    // Default matches the deployment name used by FikaFinans.Infrastructure.Tests
    // (see FikaFinans.Infrastructure.Foundry.FoundryModelIds). Override per
    // environment via the FOUNDRY_MACRO_MODEL_ID user-secret.
    private const string DefaultModelId = "gpt-5.4-1";

    private AIProjectClient _projectClient = null!;
    private string _modelId = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<MacroAnalystAgentIntegrationTests>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live MacroAnalyst test. " +
                $"Run: dotnet user-secrets set {EndpointKey} https://<your-foundry-project> " +
                $"--project FikaFinans.InfrastructureV2.Tests");
        }

        _modelId = config[ModelIdKey] ?? DefaultModelId;

        var clientOptions = new AIProjectClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(3),
        };
        _projectClient = new AIProjectClient(new Uri(endpoint!), new DefaultAzureCredential(), clientOptions);
    }

    [Test]
    public async Task RunInMemoryAsync_RealFoundry_ProducesValidMacroContext()
    {
        var summary = JsonSerializer.Deserialize<WeeklySummaryRun>(
            await File.ReadAllTextAsync(Paths.AnalyticsWeeklySummaryJsonAbs),
            AnalyticsJsonOptions.Default)!;
        var chain = JsonSerializer.Deserialize<SubstitutionChainRun>(
            await File.ReadAllTextAsync(Paths.AnalyticsSubstitutionChainJsonAbs),
            AnalyticsJsonOptions.Default)!;
        var targets = JsonSerializer.Deserialize<OpportunityScanRun>(
            await File.ReadAllTextAsync(Paths.AnalyticsRotationTargetsJsonAbs),
            AnalyticsJsonOptions.Default)!;

        var dl = MakeSyntheticDataLoaderOutput(summary.PeriodIsoWeek);

        var llm = new FoundryMacroLlmClient(_projectClient, _modelId);
        var sut = new MacroAnalystAgent(llm);

        var ctx = await sut.RunInMemoryAsync(summary, chain, targets, dl, summary.PeriodIsoWeek);

        Assert.Multiple(() =>
        {
            Assert.That(ctx.IsoWeek, Is.EqualTo(summary.PeriodIsoWeek));
            Assert.That(ctx.NetMoodInput, Is.EqualTo(summary.NetMood));
            Assert.That(ctx.SourceRunIds.WeeklySummaryRunId, Is.EqualTo(summary.RunId));
            Assert.That(ctx.SourceRunIds.SubstitutionChainRunId, Is.EqualTo(chain.RunId));
            Assert.That(ctx.SourceRunIds.RotationTargetsRunId, Is.EqualTo(targets.RunId));
            Assert.That(ctx.RegimeConfidence, Is.InRange(0m, 1m));
        });

        // Persist for inspection.
        var outPath = Paths.MacroAnalystOutput(summary.PeriodIsoWeek, "integration");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath,
            JsonSerializer.Serialize(ctx, DataLoaderJsonOptions.Default));
        TestContext.Out.WriteLine($"MacroContext written to {outPath}");
    }

    private static DataLoaderOutput MakeSyntheticDataLoaderOutput(string isoWeek)
    {
        // Curated category set that plausibly maps to the 2026-W17 analytics narrative
        // (AI/semis, Energy, broad equity). Categories follow Avanza's Swedish taxonomy.
        var categories = new[]
        {
            "Branschfond, Energi",
            "Branschfond, Teknik",
            "Globalfond",
            "USA-fond",
            "Tillvxtmarknadsfond",
        };

        var funds = categories.Select((cat, i) => new FundRecord
        {
            Isin = $"SE{i:D10}",
            Metadata = new FundMetadata
            {
                Isin                     = $"SE{i:D10}",
                Name                     = $"Synthetic Fund {i}",
                CompanyName              = "Synthetic",
                CurrencyCode             = "SEK",
                Category                 = cat,
                FundType                 = "Equity",
                IsIndexFund              = false,
                ManagedType              = "Active",
                TotalFee                 = 1.0m,
                ManagementFee            = 0.8m,
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
        }).ToList();

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = isoWeek,
            Family          = "synthetic",
            RunId           = "macro-integration",
            ConfigVersion   = "1.0.0",
            Funds           = funds,
            FrozenPositions = new List<FrozenPosition>(),
            CashAvailableKr = 0m,
            DataQuality     = new DataQuality(),
        };
    }
}
