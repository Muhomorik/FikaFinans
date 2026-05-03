using FikaFinans.InfrastructureV2.Tests.Models.Recommender;

namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class RejectedRecommendation
{
    public required string Isin { get; init; }
    public required Recommendation SourceRecommendation { get; init; }
    public required string RejectedBecause { get; init; }
    public required string WouldHaveBeen { get; init; }
}
