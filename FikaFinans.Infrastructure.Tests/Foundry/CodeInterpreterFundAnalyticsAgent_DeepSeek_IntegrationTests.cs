using System.Text.RegularExpressions;
using Azure.AI.Extensions.OpenAI;
using FikaFinans.Application.Agents;
using FikaFinans.Infrastructure.Foundry;
using FikaFinans.Infrastructure.Prompts;
using NLog;

namespace FikaFinans.Infrastructure.Tests.Foundry;

/// <summary>
/// Live end-to-end of <see cref="CodeInterpreterFundAnalyticsAgent"/> against the deployed
/// DeepSeek-R1-0528 model. The model card lists <c>Tool calling: No</c> — DeepSeek accepts
/// the Code Interpreter tool definition but never invokes it, so we deliberately avoid
/// asserting the row count "3" appears in the response (the gpt fixture's stronger check).
/// What we still verify: the agent round-trips against Foundry, picks up the right model id,
/// and reports non-zero tokens + elapsed. If a future DeepSeek update flips tool calling on,
/// tighten the assertion to mirror the gpt fixture.
/// </summary>
[TestFixture]
[Explicit("Burns Foundry tokens + Code-Interpreter session ($0.03+ per run).")]
[Category("Integration")]
[Category("Foundry")]
[Category("Costly")]
public sealed class CodeInterpreterFundAnalyticsAgent_DeepSeek_IntegrationTests : FoundryIntegrationTestBase
{
    private string _tempDataFolder = null!;
    private string _tempSidecarPath = null!;
    private FoundryFileStore _fileStore = null!;
    private FundDataFileSet _fileSet = null!;

    [SetUp]
    public void SetUp()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"FikaFinans-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        _tempDataFolder = Path.Combine(scratch, "data");
        Directory.CreateDirectory(_tempDataFolder);
        _tempSidecarPath = Path.Combine(scratch, "foundry-files.json");

        foreach (var name in FundDataFiles.All)
        {
            File.Copy(Path.Combine(TestDataDir, name), Path.Combine(_tempDataFolder, name));
        }

        _fileSet = FundDataFileSet.FromFolder(_tempDataFolder);
        _fileStore = new FoundryFileStore(
            logger: LogManager.GetLogger(nameof(CodeInterpreterFundAnalyticsAgent_DeepSeek_IntegrationTests)),
            projectClient: ProjectClient,
            sidecarPath: _tempSidecarPath);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (File.Exists(_tempSidecarPath))
        {
            var fileClient = ProjectClient.ProjectOpenAIClient.GetOpenAIFileClient();
            var json = await File.ReadAllTextAsync(_tempSidecarPath);
            foreach (Match match in Regex.Matches(json, "assistant-[A-Za-z0-9]+"))
            {
                try { await fileClient.DeleteFileAsync(match.Value); }
                catch { /* best effort */ }
            }
        }

        var scratch = Path.GetDirectoryName(_tempSidecarPath);
        if (scratch is not null && Directory.Exists(scratch))
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch { /* file lock — not fatal */ }
        }
    }

    [Test]
    public async Task RunAsync_DeepSeekModel_ReturnsResponseWithTokens()
    {
        var sut = new CodeInterpreterFundAnalyticsAgent(
            logger: LogManager.GetLogger(nameof(CodeInterpreterFundAnalyticsAgent_DeepSeek_IntegrationTests)),
            projectClient: ProjectClient,
            fileStore: _fileStore,
            promptProvider: new EmbeddedPromptProvider(),
            fileSet: _fileSet,
            timeProvider: TimeProvider.System,
            modelId: FoundryModelIds.DeepSeekR1_0528_1);

        var run = await sut.RunAsync(
            "Just answer with the count: how many rows does positions.csv have? Use Code Interpreter to read it.");

        Assert.Multiple(() =>
        {
            Assert.That(run.ModelId, Is.EqualTo(FoundryModelIds.DeepSeekR1_0528_1));
            Assert.That(run.ResponseText, Is.Not.Null.And.Not.Empty);
            Assert.That(run.InputTokens, Is.GreaterThan(0));
            Assert.That(run.OutputTokens, Is.GreaterThan(0));
            Assert.That(run.ElapsedMs, Is.GreaterThan(0));
            // Intentionally no Does.Contain("3"): DeepSeek-R1-0528 doesn't invoke tools, so the
            // answer is whatever the model guesses without ever opening the file.
        });
    }
}
