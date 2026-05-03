using Azure.AI.Projects;
using Azure.Identity;

using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;

using Microsoft.Extensions.Configuration;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Tests;

/// <summary>
/// Shared test base for the fund-pipeline sandbox. Resolves the Foundry endpoint
/// from user-secrets, exposes a pre-built <see cref="AIProjectClient"/>, and points
/// at the <c>TestDataSbd</c> fixture (16-position real fund data) and the
/// <c>out_logs</c> folder for inspection artifacts.
/// </summary>
public abstract class FundPipelineSandboxTestBase
{
    protected const string EndpointKey = "FOUNDRY_PROJECT_ENDPOINT";

    protected AIProjectClient ProjectClient { get; private set; } = null!;
    protected string FixtureFolder { get; private set; } = null!;
    protected string PromptFolder { get; private set; } = null!;
    protected string SignalsFixturePath { get; private set; } = null!;
    protected string OutLogsDir { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp_ResolveEndpointAndPaths()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<FundPipelineSandboxTestBase>()
            .Build();

        var endpoint = config[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Ignore(
                $"{EndpointKey} not set in user-secrets — skipping live Foundry sandbox test. " +
                $"Run: dotnet user-secrets set {EndpointKey} https://<your-foundry-project> " +
                $"--project FikaFinans.Infrastructure.Tests");
        }

        // Mirror InfrastructureModule.BuildAIProjectClient: a 10-minute NetworkTimeout per
        // attempt so the SDK doesn't retry-loop while a Code Interpreter response is still
        // streaming. Without this the default (~100s per attempt × retries) cancels the
        // request at ~8.5 min before our agent's outer RunTimeout token fires.
        var clientOptions = new AIProjectClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };
        ProjectClient = new AIProjectClient(new Uri(endpoint!), new DefaultAzureCredential(), clientOptions);

        var testDir = TestContext.CurrentContext.TestDirectory;
        FixtureFolder = Path.Combine(testDir, "TestDataSbd");
        Assert.That(Directory.Exists(FixtureFolder),
            $"TestDataSbd folder missing at {FixtureFolder} — check csproj CopyToOutputDirectory.");

        PromptFolder = Path.Combine(testDir, "FundPipelineSandbox", "Prompts");
        Assert.That(Directory.Exists(PromptFolder),
            $"FundPipelineSandbox/Prompts missing at {PromptFolder} — check csproj CopyToOutputDirectory.");

        SignalsFixturePath = Path.Combine(testDir, "FundPipelineSandbox", "Fixtures", "sample_fund_signals.json");
        Assert.That(File.Exists(SignalsFixturePath),
            $"sample_fund_signals.json missing at {SignalsFixturePath} — check csproj CopyToOutputDirectory.");

        // out_logs lives at the project root (next to the .csproj), not in bin/Debug/, so
        // artifacts persist across rebuilds and are easy to find for review.
        OutLogsDir = Path.Combine(ResolveProjectRoot(testDir), "out_logs");
        Directory.CreateDirectory(OutLogsDir);
    }

    protected string LoadPrompt(string fileName) =>
        File.ReadAllText(Path.Combine(PromptFolder, fileName));

    protected string OutLogPath(string baseName, string extension = "json")
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(OutLogsDir, $"{baseName}-{stamp}.{extension}");
    }

    /// <summary>
    /// Runs <paramref name="work"/> and, if it throws <see cref="SandboxResponseParseException"/>,
    /// dumps the raw model response (and partial JSON, if any) to <c>out_logs/</c> before
    /// re-throwing. Without this we lose the entire 5-minute Code Interpreter run on a parse
    /// failure — only a 500-char preview survives in the inner exception.
    /// </summary>
    protected async Task<T> RunAndDumpOnParseFailureAsync<T>(Func<Task<T>> work, string failureBaseName)
    {
        try
        {
            return await work();
        }
        catch (SandboxResponseParseException ex)
        {
            var rawPath = OutLogPath($"{failureBaseName}-raw-failed", "txt");
            await File.WriteAllTextAsync(rawPath, ex.RawResponseText);
            TestContext.Out.WriteLine($"PARSE FAILURE — raw response → {rawPath}");

            if (ex.ExtractedJson is not null)
            {
                var jsonPath = OutLogPath($"{failureBaseName}-extracted-failed", "json");
                await File.WriteAllTextAsync(jsonPath, ex.ExtractedJson);
                TestContext.Out.WriteLine($"PARSE FAILURE — partial JSON → {jsonPath}");
            }

            throw;
        }
    }

    private static string ResolveProjectRoot(string testDir)
    {
        var dir = new DirectoryInfo(testDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FikaFinans.Infrastructure.Tests.csproj")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"unable to locate FikaFinans.Infrastructure.Tests.csproj walking up from {testDir}");
    }
}
