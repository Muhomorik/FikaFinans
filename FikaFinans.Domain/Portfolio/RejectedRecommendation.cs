using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Portfolio;

namespace FikaFinans.Domain.Portfolio;

public sealed class RejectedRecommendation
{
    public required Isin Isin { get; init; }
    public required Recommendation SourceRecommendation { get; init; }
    public required string RejectedBecause { get; init; }
    public required string WouldHaveBeen { get; init; }
}
