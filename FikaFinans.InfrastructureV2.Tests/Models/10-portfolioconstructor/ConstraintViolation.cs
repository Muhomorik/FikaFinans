using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class ConstraintViolation
{
    // Contract names this field literally "type".
    [JsonPropertyName("type")]
    public required string ViolationType { get; init; }

    public string? Isin { get; init; }
    public string? Value { get; init; }
    public required string Message { get; init; }
}
