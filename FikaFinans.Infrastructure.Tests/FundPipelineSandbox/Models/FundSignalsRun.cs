using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>Step 5 output envelope. Serialized to <c>analytics-fund-signals.json</c>.</summary>
public sealed record FundSignalsRun(
    [property: JsonPropertyName("generated_at")] string GeneratedAt,
    [property: JsonPropertyName("data_period_end")] string DataPeriodEnd,
    [property: JsonPropertyName("macro_regime")] string MacroRegime,
    [property: JsonPropertyName("signals")] IReadOnlyList<FundSignal> Signals);
