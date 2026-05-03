using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.Recommender;

[JsonConverter(typeof(JsonStringEnumConverter<Recommendation>))]
public enum Recommendation
{
    [JsonStringEnumMemberName("CatalystEntry")] CatalystEntry,
    [JsonStringEnumMemberName("MomentumEntry")] MomentumEntry,
    [JsonStringEnumMemberName("ThesisExit")]    ThesisExit,
    [JsonStringEnumMemberName("MomentumExit")]  MomentumExit,
    [JsonStringEnumMemberName("Maintain")]      Maintain,
    [JsonStringEnumMemberName("Skip")]          Skip,
}
