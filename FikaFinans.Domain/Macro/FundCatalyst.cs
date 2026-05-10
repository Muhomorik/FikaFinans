using FikaFinans.Domain.Macro;

namespace FikaFinans.Domain.Macro;

public sealed class FundCatalyst
{
    public required string Name { get; init; }
    public required Intensity Intensity { get; init; }
    public required int WeeksActive { get; init; }
    public required ExposureType ExposureType { get; init; }
    public required string Rationale { get; init; }
}
