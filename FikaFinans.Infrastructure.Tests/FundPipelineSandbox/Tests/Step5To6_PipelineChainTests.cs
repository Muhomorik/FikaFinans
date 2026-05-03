using System.Text.Json;

using FikaFinans.Application.Agents;
using FikaFinans.Infrastructure.Foundry;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

using NLog;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Tests;

/// <summary>
/// End-to-end Step 5 → JSON-on-disk → Step 6 chain. Drives the full sandbox
/// with the real reduced fund-data fixture (TestDataSbd) and persists every
/// JSON artifact to <c>out_logs/</c> for review.
/// </summary>
[TestFixture]
[Explicit("Burns Foundry tokens + Code-Interpreter session ($0.04+ per run — Step 5 + Step 6 combined).")]
[Category("Integration")]
[Category("Foundry")]
[Category("Costly")]
[Category("FundPipelineSandbox")]
public sealed class Step5To6_PipelineChainTests : FundPipelineSandboxTestBase
{
    private const decimal CashAvailableKr = 50_000m;

    [Test]
    public async Task Step5To6_RunFullChain_GptModel_ProducesActionableOutput()
    {
        var step5Prompt = LoadPrompt("fund_signals.prompt.md");
        var step6Prompt = LoadPrompt("action_consolidation.prompt.md");

        var step5Agent = new FundSignalScoringAgent(
            ProjectClient,
            FoundryModelIds.Gpt5_4_1,
            step5Prompt,
            LogManager.GetLogger("ChainStep5"));

        var step6Agent = new ActionConsolidationAgent(
            ProjectClient,
            FoundryModelIds.Gpt5_4_1,
            step6Prompt,
            LogManager.GetLogger("ChainStep6"));

        var fileClient = ProjectClient.ProjectOpenAIClient.GetOpenAIFileClient();
        await using var uploader = new SandboxFileUploader(fileClient);

        // ── Step 5: scoring ─────────────────────────────────────────────────────
        var step5Result = await RunAndDumpOnParseFailureAsync(
            () => step5Agent.RunAsync(
                new Step5Inputs(FixtureFolder, FoundryModelIds.Gpt5_4_1),
                uploader),
            "chain-step5");

        var step5JsonPath = OutLogPath("chain-step5-fund-signals");
        var step5RawPath = OutLogPath("chain-step5-raw", "txt");
        await File.WriteAllTextAsync(step5JsonPath, JsonResponse.PrettyPrint(step5Result.ExtractedJson));
        await File.WriteAllTextAsync(step5RawPath, step5Result.RawResponseText);
        TestContext.Out.WriteLine($"Step 5 JSON → {step5JsonPath}");
        TestContext.Out.WriteLine(
            $"Step 5 tokens in/out = {step5Result.InputTokens}/{step5Result.OutputTokens}, elapsed = {step5Result.ElapsedMs}ms");

        Assert.That(step5Result.Run.Signals, Is.Not.Empty, "Step 5 produced no signals");

        // ── Step 6: consolidation ───────────────────────────────────────────────
        var positionsPath = Path.Combine(FixtureFolder, FundDataFiles.Positions);
        var structurePath = Path.Combine(FixtureFolder, FundDataFiles.Structure);

        var step6Result = await RunAndDumpOnParseFailureAsync(
            () => step6Agent.RunAsync(new Step6Inputs(
                SignalsJsonPath: step5JsonPath,
                PositionsCsvPath: positionsPath,
                PortfolioStructurePath: structurePath,
                CashAvailableKr: CashAvailableKr,
                ModelDeploymentName: FoundryModelIds.Gpt5_4_1)),
            "chain-step6");

        var step6JsonPath = OutLogPath("chain-step6-actions");
        var step6RawPath = OutLogPath("chain-step6-raw", "txt");
        await File.WriteAllTextAsync(step6JsonPath, JsonResponse.PrettyPrint(step6Result.ExtractedJson));
        await File.WriteAllTextAsync(step6RawPath, step6Result.RawResponseText);
        TestContext.Out.WriteLine($"Step 6 JSON → {step6JsonPath}");
        TestContext.Out.WriteLine(
            $"Step 6 tokens in/out = {step6Result.InputTokens}/{step6Result.OutputTokens}, elapsed = {step6Result.ElapsedMs}ms");

        var summary = step6Result.Run.CapitalSummary;

        Assert.Multiple(() =>
        {
            Assert.That(step6Result.Run.Actions, Is.Not.Null);

            // Capital math must balance regardless of how the model populated the rows.
            Assert.That(summary.CashAvailableKr, Is.EqualTo(CashAvailableKr),
                "cash_available must echo back the test parameter");
            Assert.That(summary.CashAvailableKr + summary.SellProceedsKr,
                Is.EqualTo(summary.TotalDeployableKr).Within(0.01m),
                "cash + sell proceeds must equal total_deployable");
            Assert.That(summary.TotalDeployableKr - summary.TotalBuyAmountKr,
                Is.EqualTo(summary.CashRemainingKr).Within(0.01m),
                "total_deployable - total_buy must equal cash_remaining");

            // Pinned funds (per portfolio_structure.md in TestDataSbd) must never be sold.
            // The fixture pins "Storebrand Global All Countries", "Storebrand Global Solutions" (core),
            // and "Swedbank Robur Rysslandsfond" (writeoff).
            string[] pinnedNames =
            [
                "Storebrand Global All Countries",
                "Storebrand Global Solutions",
                "Swedbank Robur Rysslandsfond",
            ];

            foreach (var pinned in pinnedNames)
            {
                Assert.That(step6Result.Run.Actions, Has.None.Matches<ConsolidatedAction>(a =>
                    a.Action == ActionType.Sell &&
                    a.FundName.Contains(pinned, StringComparison.OrdinalIgnoreCase)),
                    $"pinned fund '{pinned}' must not be sold");
            }

            // Combined token spend log line for cost tracking.
            var totalIn = step5Result.InputTokens + step6Result.InputTokens;
            var totalOut = step5Result.OutputTokens + step6Result.OutputTokens;
            TestContext.Out.WriteLine($"chain total tokens in/out = {totalIn}/{totalOut}");
        });
    }
}
