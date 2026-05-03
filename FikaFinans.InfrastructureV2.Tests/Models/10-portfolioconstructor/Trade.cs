using System.Text.Json.Serialization;
using FikaFinans.InfrastructureV2.Tests.Models.Recommender;

namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class Trade
{
    public required string Isin { get; init; }
    public required string FundName { get; init; }

    // Contract names this field literally "trade" (not "trade_type").
    [JsonPropertyName("trade")]
    public required TradeType TradeType { get; init; }

    public required string TradeReason { get; init; }
    public decimal AmountKr { get; init; }
    public required Recommendation SourceRecommendation { get; init; }
    public decimal SourceConviction { get; init; }
    public string? RotationPairId { get; init; }
    public string? TrimReason { get; init; }
    public decimal ScalingFactor { get; init; } = 1.0m;
    public required IReadOnlyList<string> AuditNotes { get; init; }
}
