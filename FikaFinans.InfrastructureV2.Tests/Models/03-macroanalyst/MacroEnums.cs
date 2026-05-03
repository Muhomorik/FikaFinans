using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

[JsonConverter(typeof(JsonStringEnumConverter<MacroRegime>))]
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
[JsonConverter(typeof(JsonStringEnumConverter<Intensity>))]
public enum Intensity
{
    [JsonStringEnumMemberName("low")]    Low,
    [JsonStringEnumMemberName("medium")] Medium,
    [JsonStringEnumMemberName("high")]   High,
}
