using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

[JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
public enum RunStatus
{
    Success,
    Partial,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<MarketSentiment>))]
public enum MarketSentiment
{
    RiskOn,
    RiskOff,
    Mixed,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConfidenceLevel>))]
public enum ConfidenceLevel
{
    High,
    Medium,
    Low,
}

[JsonConverter(typeof(JsonStringEnumConverter<SignalStrength>))]
public enum SignalStrength
{
    Strong,
    Moderate,
    Weak,
}
