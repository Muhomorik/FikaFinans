namespace FikaFinans.Application.Pipeline.Configs;

public sealed class UniverseEnricherConfig
{
    public ConvictionWeights Weights { get; init; } = new();
    public WeightValidation WeightValidation { get; init; } = new();
    public MetricsQualityComponents MetricsQualityComponents { get; init; } = new();
    public AlternativesPolicy Alternatives { get; init; } = new();
    public RotationPairingPolicy RotationPairing { get; init; } = new();
    public TieBreakPolicy TieBreak { get; init; } = new();
    public string UniverseRankWithin { get; init; } = "recommendation_type";

    public static UniverseEnricherConfig Default => new();
}

public sealed class ConvictionWeights
{
    public decimal SignalStrength { get; init; } = 0.25m;
    public decimal MetricsQuality { get; init; } = 0.25m;
    public decimal MacroAlignment { get; init; } = 0.15m;
    public decimal ThesisValidity { get; init; } = 0.20m;
    public decimal UniverseContext { get; init; } = 0.15m;

    public decimal Sum() =>
        SignalStrength + MetricsQuality + MacroAlignment + ThesisValidity + UniverseContext;
}

public sealed class WeightValidation
{
    public decimal MustSumTo { get; init; } = 1.0m;
    public decimal Tolerance { get; init; } = 0.001m;
}

public sealed class MetricsQualityComponents
{
    public decimal Sharpe12wNormalizationMax { get; init; } = 5.0m;
    public decimal DrawdownPenaltyThresholdPct { get; init; } = -3.0m;
    public decimal VolPenaltyMinPct { get; init; } = 25.0m;
}

public sealed class AlternativesPolicy
{
    public int MaxPerFund { get; init; } = 3;
    public bool MatchWithinCategory { get; init; } = true;
    public IReadOnlyList<string> DifferentiatorDimensions { get; init; } =
        new[] { "fee", "sharpe_12w", "ann_volatility_12w_pct", "country_tilt" };
}

public sealed class RotationPairingPolicy
{
    public string MatchOn { get; init; } = "matched_theme_id";
    public string IdFormat { get; init; } = "rot_{iso_week}_{letter}";
    public string LetterAssignment { get; init; } = "alphabetical_by_largest_theme_first";
}

public sealed class TieBreakPolicy
{
    public string Primary { get; init; } = "lower_total_fee";
    public string Secondary { get; init; } = "isin_alphabetical";
}
