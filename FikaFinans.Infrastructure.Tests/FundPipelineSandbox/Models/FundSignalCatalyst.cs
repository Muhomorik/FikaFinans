using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Catalyst block populated only when <see cref="FundSignal.Label"/> is
/// <see cref="SignalLabel.CatalystEntry"/>; null otherwise.
/// </summary>
public sealed record FundSignalCatalyst(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("causal_chain")] string CausalChain,
    [property: JsonPropertyName("nav_at_catalyst")] decimal NavAtCatalyst,
    [property: JsonPropertyName("current_nav")] decimal CurrentNav,
    [property: JsonPropertyName("invalidation_condition")] string InvalidationCondition);
