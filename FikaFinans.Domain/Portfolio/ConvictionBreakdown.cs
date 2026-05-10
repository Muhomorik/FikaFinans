namespace FikaFinans.Domain.Portfolio;

public sealed class ConvictionBreakdown
{
    public required decimal SignalStrength { get; init; }
    public required decimal MetricsQuality { get; init; }
    public required decimal MacroAlignment { get; init; }
    public required decimal ThesisValidity { get; init; }
    public required decimal UniverseContext { get; init; }
}
