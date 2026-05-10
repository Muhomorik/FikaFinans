using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Portfolio;

namespace FikaFinans.Domain.Funds;

public sealed class FundRecord
{
    public required Isin Isin { get; init; }
    public required FundMetadata Metadata { get; init; }
    public required IReadOnlyList<NavBucket> NavBuckets { get; init; }
    public FundSnapshot? Snapshot { get; init; }
    public bool CurrentlyHeld { get; init; }
    public decimal? CurrentValueKr { get; init; }
    public decimal? CostBasisKr { get; init; }
    public FundLayer Layer { get; init; }

    // Step 02 enrichment — null after step 01, populated after step 02.
    public Metrics? Metrics { get; init; }

    // Step 04 enrichment — null after steps 01–03, populated after step 04.
    public SignalLabel? Signal { get; init; }
    public string? RuleFired { get; init; }
    public CriteriaEvaluation? CriteriaEvaluation { get; init; }

    // Step 05 enrichment — null after steps 01–04, populated after step 05.
    public MacroAlignment? MacroAlignment { get; init; }
    public MatchedTheme? MatchedTheme { get; init; }
    public bool? PromotedToForming { get; init; }
    public string? PromotionReason { get; init; }

    // Step 06 enrichment — null after steps 01–05; populated after step 06
    // (or null if no catalyst applies).
    public FundCatalyst? Catalyst { get; init; }

    // Step 07 enrichment — null after steps 01–06; populated after step 07.
    public ThesisValidity? ThesisValidity { get; init; }
    public string? ThesisRationale { get; init; }
    public ThesisMethod? ThesisMethod { get; init; }

    // Step 08 enrichment — null after steps 01–07; populated after step 08.
    public Recommendation? Recommendation { get; init; }
    public string? RecommendationReason { get; init; }

    // Step 09 enrichment — null after steps 01–08; populated after step 09.
    public decimal? ConvictionScore { get; init; }
    public ConvictionBreakdown? ConvictionBreakdown { get; init; }
    public UniverseRank? UniverseRank { get; init; }
    public IReadOnlyList<Alternative>? Alternatives { get; init; }
    public string? RotationPairId { get; init; }
}
