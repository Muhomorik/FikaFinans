namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class TradesOutput
{
    public required string GeneratedAt { get; init; }
    public required string IsoWeek { get; init; }
    public required string ConfigVersion { get; init; }
    public required IReadOnlyList<Trade> Trades { get; init; }
    public required IReadOnlyList<RejectedRecommendation> RejectedRecommendations { get; init; }
    public required CapitalSummary CapitalSummary { get; init; }
    public required IReadOnlyList<ConstraintViolation> ConstraintViolations { get; init; }
}
