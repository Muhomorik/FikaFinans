using System.Text.Json;

using Azure.AI.Extensions.OpenAI;

using FikaFinans.Infrastructure.Foundry;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

using NLog;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Tests;

/// <summary>
/// Live Step 5 sandbox runs against TestDataSbd. Asserts schema/contract
/// properties — not specific labels per fund — since the labels depend on
/// the model's interpretation of real data.
/// </summary>
[TestFixture]
[Explicit("Burns Foundry tokens + Code-Interpreter session ($0.03+ per run).")]
[Category("Integration")]
[Category("Foundry")]
[Category("Costly")]
[Category("FundPipelineSandbox")]
public sealed class Step5_FundSignalScoringAgentTests : FundPipelineSandboxTestBase
{
    [Test]
    public async Task Step5_RunAsync_GptModel_EmitsValidSchemaWithSignals()
    {
        var promptText = LoadPrompt("fund_signals.prompt.md");
        var sut = new FundSignalScoringAgent(
            ProjectClient,
            FoundryModelIds.Gpt5_4_1,
            promptText,
            LogManager.GetLogger(nameof(Step5_FundSignalScoringAgentTests)));

        var fileClient = ProjectClient.ProjectOpenAIClient.GetOpenAIFileClient();
        await using var uploader = new SandboxFileUploader(fileClient);

        var result = await RunAndDumpOnParseFailureAsync(
            () => sut.RunAsync(new Step5Inputs(FixtureFolder, FoundryModelIds.Gpt5_4_1), uploader),
            "step5");

        // Persist artifacts for human inspection.
        var jsonOut = OutLogPath("step5-fund-signals");
        var rawOut = OutLogPath("step5-raw", "txt");
        await File.WriteAllTextAsync(jsonOut, JsonResponse.PrettyPrint(result.ExtractedJson));
        await File.WriteAllTextAsync(rawOut, result.RawResponseText);
        TestContext.Out.WriteLine($"Step 5 JSON → {jsonOut}");
        TestContext.Out.WriteLine($"Step 5 raw  → {rawOut}");
        TestContext.Out.WriteLine(
            $"tokens in/out = {result.InputTokens}/{result.OutputTokens}, elapsed = {result.ElapsedMs}ms");

        Assert.Multiple(() =>
        {
            Assert.That(result.Run.Signals, Is.Not.Empty,
                "Step 5 must emit at least one fund signal");
            Assert.That(result.Run.MacroRegime, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Run.DataPeriodEnd, Is.Not.Null.And.Not.Empty);
            Assert.That(result.InputTokens, Is.GreaterThan(0));
            Assert.That(result.OutputTokens, Is.GreaterThan(0));

            // Every signal carries a valid label and required structural fields.
            foreach (var signal in result.Run.Signals)
            {
                Assert.That(Enum.IsDefined(signal.Label), Is.True,
                    $"unknown label for {signal.Name}: {signal.Label}");
                Assert.That(signal.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(signal.Category, Is.Not.Null.And.Not.Empty);
                Assert.That(signal.Rationale, Is.Not.Null.And.Not.Empty);
                Assert.That(signal.Metrics, Is.Not.Null);

                // CatalystEntry must populate the catalyst block; other labels must not.
                if (signal.Label == SignalLabel.CatalystEntry)
                {
                    Assert.That(signal.Catalyst, Is.Not.Null,
                        $"{signal.Name}: CatalystEntry requires a populated catalyst block");
                }

                // Pass rows have NotApplicable validity; everything else must pick a real verdict.
                if (signal.Label == SignalLabel.Pass)
                {
                    Assert.That(signal.ThesisValidity, Is.EqualTo(ThesisValidity.NotApplicable));
                }
                else
                {
                    Assert.That(signal.ThesisValidity, Is.Not.EqualTo(ThesisValidity.NotApplicable),
                        $"{signal.Name}: non-Pass row must have a real thesis validity verdict");
                }
            }
        });
    }
}
