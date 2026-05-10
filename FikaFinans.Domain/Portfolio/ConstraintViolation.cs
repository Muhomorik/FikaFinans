using System.Text.Json.Serialization;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Portfolio;

public sealed class ConstraintViolation
{
    // Contract names this field literally "type".
    [JsonPropertyName("type")]
    public required string ViolationType { get; init; }

    public Isin? Isin { get; init; }
    public string? Value { get; init; }
    public required string Message { get; init; }
}
