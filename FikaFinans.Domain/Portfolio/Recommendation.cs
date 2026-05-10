using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Portfolio;

public enum Recommendation
{
    [JsonStringEnumMemberName("CatalystEntry")] CatalystEntry,
    [JsonStringEnumMemberName("MomentumEntry")] MomentumEntry,
    [JsonStringEnumMemberName("ThesisExit")]    ThesisExit,
    [JsonStringEnumMemberName("MomentumExit")]  MomentumExit,
    [JsonStringEnumMemberName("Maintain")]      Maintain,
    [JsonStringEnumMemberName("Skip")]          Skip,
}
