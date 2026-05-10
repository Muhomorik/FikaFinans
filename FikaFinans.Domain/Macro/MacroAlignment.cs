using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Macro;

public enum MacroAlignment
{
    [JsonStringEnumMemberName("Strong")]  Strong,
    [JsonStringEnumMemberName("Partial")] Partial,
    [JsonStringEnumMemberName("None")]    None,
}
