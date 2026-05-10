using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Macro;

public enum ExposureType
{
    [JsonStringEnumMemberName("Direct")]   Direct,
    [JsonStringEnumMemberName("Indirect")] Indirect,
}
