using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.CatalystTagger;

[JsonConverter(typeof(JsonStringEnumConverter<ExposureType>))]
public enum ExposureType
{
    [JsonStringEnumMemberName("Direct")]   Direct,
    [JsonStringEnumMemberName("Indirect")] Indirect,
}
