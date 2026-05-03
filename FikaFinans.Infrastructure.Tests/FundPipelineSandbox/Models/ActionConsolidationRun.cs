using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>Step 6 output envelope. Serialized to <c>analytics-actions.json</c>.</summary>
public sealed record ActionConsolidationRun(
    [property: JsonPropertyName("generated_at")] string GeneratedAt,
    [property: JsonPropertyName("actions")] IReadOnlyList<ConsolidatedAction> Actions,
    [property: JsonPropertyName("capital_summary")] CapitalSummary CapitalSummary);
