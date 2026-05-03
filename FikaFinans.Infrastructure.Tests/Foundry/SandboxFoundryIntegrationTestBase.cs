using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.Infrastructure.Tests.Foundry;

/// <summary>
/// Sandbox variant of <see cref="FoundryIntegrationTestBase"/> that points at
/// <c>TestDataSbd</c> — the reduced set of real fund data — instead of the
/// hand-crafted three-row <c>TestData</c> fixtures. Same Foundry wiring,
/// different fixture folder.
/// </summary>
public abstract class SandboxFoundryIntegrationTestBase
{
    protected const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";

    protected AIProjectClient ProjectClient { get; private set; } = null!;
    protected string TestDataDir { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp_ResolveEndpointAndClient()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<SandboxFoundryIntegrationTestBase>()
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
        TestDataDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestDataSbd");
        Assert.That(Directory.Exists(TestDataDir),
            $"TestDataSbd folder missing at {TestDataDir} — check csproj CopyToOutputDirectory.");
    }
}
