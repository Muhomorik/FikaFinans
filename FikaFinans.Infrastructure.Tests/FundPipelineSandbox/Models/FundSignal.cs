using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// One scored fund row. Fields mirror the JSON contract in
/// <c>Prompts/fund_signals.prompt.md</c>; nullable fields use C# nullables and
/// serialize as JSON <c>null</c>.
/// </summary>
public sealed record FundSignal(
    [property: JsonPropertyName("isin")] string Isin,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("label")] SignalLabel Label,
    [property: JsonPropertyName("thesis_validity")] ThesisValidity ThesisValidity,
    [property: JsonPropertyName("exposure_type")] ExposureType ExposureType,
    [property: JsonPropertyName("rotation_target_alignment")] AlignmentLevel RotationTargetAlignment,
    [property: JsonPropertyName("currently_held")] bool CurrentlyHeld,
    [property: JsonPropertyName("current_value_kr")] decimal? CurrentValueKr,
    [property: JsonPropertyName("below_threshold")] bool BelowThreshold,
    [property: JsonPropertyName("metrics")] FundSignalMetrics Metrics,
    [property: JsonPropertyName("catalyst")] FundSignalCatalyst? Catalyst,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("missing_for_upgrade")] string? MissingForUpgrade);
