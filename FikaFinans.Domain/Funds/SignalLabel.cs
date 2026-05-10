using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Funds;

public enum SignalLabel
{
    [JsonStringEnumMemberName("Strength")] Strength,
    [JsonStringEnumMemberName("Weakness")] Weakness,
    [JsonStringEnumMemberName("Forming")]  Forming,
    [JsonStringEnumMemberName("Neutral")]  Neutral,
}
