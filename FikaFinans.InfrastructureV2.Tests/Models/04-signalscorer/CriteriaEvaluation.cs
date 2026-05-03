using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;

public sealed record CriteriaEvaluation
{
    // Snake-case policy would emit "buy_3of_3_passed"; override explicitly.
    [JsonPropertyName("buy_3of3_passed")]
    public bool Buy3Of3Passed { get; init; }

    public bool BuyMaxDdPassed { get; init; }
    public bool BuyMinSharpe12wPassed { get; init; }

    public bool SellSharpe2wLt0 { get; init; }
    public bool SellDdLtThreshold { get; init; }
    public bool SellPosLe1 { get; init; }

    public bool WatchPartialWithMacroAlignment { get; init; }

    // Set by SignalScorer when exactly one of the three buy criteria failed.
    // MacroAligner reads this to decide whether a Neutral fund with Strong macro
    // alignment is eligible for the Neutral → Forming promotion.
    public string? MissingForUpgrade { get; init; }

    public IReadOnlyList<string> DataQualityWarnings { get; init; } = Array.Empty<string>();
}
