using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.ThesisValidator;

[JsonConverter(typeof(JsonStringEnumConverter<ThesisValidity>))]
public enum ThesisValidity
{
    [JsonStringEnumMemberName("Valid")]         Valid,
    [JsonStringEnumMemberName("Partial")]       Partial,
    [JsonStringEnumMemberName("Invalid")]       Invalid,
    [JsonStringEnumMemberName("NotApplicable")] NotApplicable,
}
