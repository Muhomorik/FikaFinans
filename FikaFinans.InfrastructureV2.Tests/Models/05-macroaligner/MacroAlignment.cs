using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;

[JsonConverter(typeof(JsonStringEnumConverter<MacroAlignment>))]
public enum MacroAlignment
{
    [JsonStringEnumMemberName("Strong")]  Strong,
    [JsonStringEnumMemberName("Partial")] Partial,
    [JsonStringEnumMemberName("None")]    None,
}
