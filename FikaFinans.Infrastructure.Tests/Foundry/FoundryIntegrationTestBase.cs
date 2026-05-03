using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.Infrastructure.Tests.Foundry;

/// <summary>
/// Shared bootstrap for Foundry integration fixtures: reads
/// <c>FOUNDRY_PROJECT_ENDPOINT</c> from user-secrets and exposes a real
/// <see cref="AIProjectClient"/> against the deployed project. Tests inheriting
/// from this fixture should be marked <c>[Explicit]</c> — they hit live Azure
/// and burn tokens.
/// </summary>
public abstract class FoundryIntegrationTestBase
{
    protected const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";

    protected AIProjectClient ProjectClient { get; private set; } = null!;
    protected string TestDataDir { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp_ResolveEndpointAndClient()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<FoundryIntegrationTestBase>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live Foundry test. " +
                $"Run: dotnet user-secrets set {EndpointKey} https://<your-foundry-project> " +
                $"--project FikaFinans.Infrastructure.Tests");
        }

        ProjectClient = new AIProjectClient(new Uri(endpoint!), new DefaultAzureCredential());
        TestDataDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData");
        Assert.That(Directory.Exists(TestDataDir),
            $"TestData folder missing at {TestDataDir} — check csproj CopyToOutputDirectory.");
    }
}
