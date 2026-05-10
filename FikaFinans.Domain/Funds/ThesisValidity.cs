using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Funds;

public enum ThesisValidity
{
    [JsonStringEnumMemberName("Valid")]         Valid,
    [JsonStringEnumMemberName("Partial")]       Partial,
    [JsonStringEnumMemberName("Invalid")]       Invalid,
    [JsonStringEnumMemberName("NotApplicable")] NotApplicable,
}
