using FikaFinans.Infrastructure.Foundry;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;
using FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

using NLog;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Tests;

/// <summary>
/// Step 6 sandbox tests that feed a hand-crafted <c>sample_fund_signals.json</c>
/// fixture so the agent's behaviour is independent of Step 5.
/// </summary>
[TestFixture]
[Explicit("Burns Foundry tokens (~$0.005 per run — cheaper than Step 5, no Code Interpreter).")]
[Category("Integration")]
[Category("Foundry")]
[Category("FundPipelineSandbox")]
public sealed class Step6_ActionConsolidationAgentTests : FundPipelineSandboxTestBase
{
    private const decimal CashAvailableKr = 50_000m;

    [Test]
    public async Task Step6_RunAsync_GptModel_OrdersSellsBeforeBuysAndBalancesCapital()
    {
        var promptText = LoadPrompt("action_consolidation.prompt.md");
        var sut = new ActionConsolidationAgent(
            ProjectClient,
            FoundryModelIds.Gpt5_4_1,
            promptText,
            LogManager.GetLogger(nameof(Step6_ActionConsolidationAgentTests)));

        // Resolve fixture-specific positions/structure that match the IDs in sample_fund_signals.json.
        var fixturesDir = Path.GetDirectoryName(SignalsFixturePath)!;
        var positionsPath = Path.Combine(fixturesDir, "positions_with_pinned.csv");
        var structurePath = Path.Combine(fixturesDir, "portfolio_structure_with_pinned.md");

        var inputs = new Step6Inputs(
            SignalsJsonPath: SignalsFixturePath,
            PositionsCsvPath: positionsPath,
            PortfolioStructurePath: structurePath,
            CashAvailableKr: CashAvailableKr,
            ModelDeploymentName: FoundryModelIds.Gpt5_4_1);

        var result = await RunAndDumpOnParseFailureAsync(() => sut.RunAsync(inputs), "step6");

        var jsonOut = OutLogPath("step6-actions");
        var rawOut = OutLogPath("step6-raw", "txt");
        await File.WriteAllTextAsync(jsonOut, JsonResponse.PrettyPrint(result.ExtractedJson));
        await File.WriteAllTextAsync(rawOut, result.RawResponseText);
        TestContext.Out.WriteLine($"Step 6 JSON → {jsonOut}");
        TestContext.Out.WriteLine($"Step 6 raw  → {rawOut}");
        TestContext.Out.WriteLine(
            $"tokens in/out = {result.InputTokens}/{result.OutputTokens}, elapsed = {result.ElapsedMs}ms");

        var actions = result.Run.Actions.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(actions, Is.Not.Empty, "Step 6 must emit at least one action");

            // Sells before buys, buys before holds.
            var firstBuyIdx = actions.FindIndex(a => a.Action == ActionType.Buy);
            var lastSellIdx = actions.FindLastIndex(a => a.Action == ActionType.Sell);
            if (lastSellIdx >= 0 && firstBuyIdx >= 0)
            {
                Assert.That(lastSellIdx, Is.LessThan(firstBuyIdx),
                    "every Sell must appear before every Buy");
            }

            // Pinned core fund must never be sold.
            Assert.That(actions, Has.None.Matches<ConsolidatedAction>(a =>
                a.Action == ActionType.Sell && a.FundName.Contains("Pinned Core", StringComparison.OrdinalIgnoreCase)),
                "pinned core funds must not be sold");

            // BuySignal (20 000 kr) appears before CatalystEntry (10 000 kr) in the buy block.
            var buyAmounts = actions.Where(a => a.Action == ActionType.Buy)
                .Select(a => a.AmountKr).ToList();
            if (buyAmounts.Count >= 2)
            {
                var firstFullBuyIdx = buyAmounts.FindIndex(amt => amt == 20_000m);
                var firstHalfBuyIdx = buyAmounts.FindIndex(amt => amt == 10_000m);
                if (firstFullBuyIdx >= 0 && firstHalfBuyIdx >= 0)
                {
                    Assert.That(firstFullBuyIdx, Is.LessThan(firstHalfBuyIdx),
                        "BuySignal rows (20000 kr) must appear before CatalystEntry rows (10000 kr)");
                }
            }

            // No more than 3 Buy rows total.
            var buyCount = actions.Count(a => a.Action == ActionType.Buy);
            Assert.That(buyCount, Is.LessThanOrEqualTo(3), "max 3 buys per consolidation");

            // Capital math identity: cash + sell_proceeds == total_deployable.
            var summary = result.Run.CapitalSummary;
            Assert.That(summary.CashAvailableKr, Is.EqualTo(CashAvailableKr),
                "cash_available must echo back the test parameter");
            Assert.That(summary.CashAvailableKr + summary.SellProceedsKr,
                Is.EqualTo(summary.TotalDeployableKr).Within(0.01m),
                "cash + sell proceeds must equal total_deployable");
            Assert.That(summary.TotalDeployableKr - summary.TotalBuyAmountKr,
                Is.EqualTo(summary.CashRemainingKr).Within(0.01m),
                "total_deployable - total_buy must equal cash_remaining");

            // Steps must be 1-based and contiguous.
            for (var i = 0; i < actions.Count; i++)
            {
                Assert.That(actions[i].Step, Is.EqualTo(i + 1),
                    $"action steps must be 1-based and contiguous; row {i} has step {actions[i].Step}");
            }

            Assert.That(result.InputTokens, Is.GreaterThan(0));
            Assert.That(result.OutputTokens, Is.GreaterThan(0));
        });
    }
}
