using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;

[JsonConverter(typeof(JsonStringEnumConverter<SignalLabel>))]
public enum SignalLabel
{
    [JsonStringEnumMemberName("Strength")] Strength,
    [JsonStringEnumMemberName("Weakness")] Weakness,
    [JsonStringEnumMemberName("Forming")]  Forming,
    [JsonStringEnumMemberName("Neutral")]  Neutral,
}
