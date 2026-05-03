using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Aggregated metrics over the last 3 windows for one fund. All numeric fields
/// are emitted as numbers (not strings) — see <c>Prompts/fund_signals.prompt.md</c>.
/// Nullable because Code Interpreter legitimately emits <c>null</c> when a metric
/// is undefined (zero-variance Sharpe, insufficient history for volatility, etc.).
/// </summary>
public sealed record FundSignalMetrics(
    [property: JsonPropertyName("windows_positive")] string WindowsPositive,
    [property: JsonPropertyName("current_drawdown_pct")] decimal? CurrentDrawdownPct,
    [property: JsonPropertyName("latest_sharpe")] decimal? LatestSharpe,
    [property: JsonPropertyName("ann_volatility_pct")] decimal? AnnVolatilityPct,
    [property: JsonPropertyName("net_return_after_fee_pct")] decimal? NetReturnAfterFeePct,
    [property: JsonPropertyName("total_fee_pct")] decimal? TotalFeePct);
