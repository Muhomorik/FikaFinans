using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Capital math footer for the action consolidation. Identity required:
/// <c>cash_available + sell_proceeds == total_deployable</c> and
/// <c>total_deployable - total_buy_amount == cash_remaining</c>.
/// </summary>
public sealed record CapitalSummary(
    [property: JsonPropertyName("cash_available_kr")] decimal CashAvailableKr,
    [property: JsonPropertyName("sell_proceeds_kr")] decimal SellProceedsKr,
    [property: JsonPropertyName("total_deployable_kr")] decimal TotalDeployableKr,
    [property: JsonPropertyName("total_buy_amount_kr")] decimal TotalBuyAmountKr,
    [property: JsonPropertyName("cash_remaining_kr")] decimal CashRemainingKr);
