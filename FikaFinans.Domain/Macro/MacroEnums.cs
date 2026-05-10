using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Macro;

public enum MacroRegime
{
    RiskOn,
    RiskOff,
    Mixed,
    Stagflation,
    Crisis,
}

// `intensity` values on the wire are lowercase per the contract:
// "low" | "medium" | "high".
public enum Intensity
{
    [JsonStringEnumMemberName("low")]    Low,
    [JsonStringEnumMemberName("medium")] Medium,
    [JsonStringEnumMemberName("high")]   High,
}
