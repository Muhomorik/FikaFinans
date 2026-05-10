namespace FikaFinans.Application.Pipeline.Llm;

public sealed class CatalystExposureClassification
{
    public required string CatalystName { get; init; }
    public required ExposureKind Exposure { get; init; }
    public string? Rationale { get; init; }
}

public enum ExposureKind
{
    None,
    Indirect,
    Direct,
}
