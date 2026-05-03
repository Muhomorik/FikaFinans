namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

// One LLM verdict for a single (fund, catalyst) pair. The LLM may return Direct,
// Indirect, or None; the agent filters out None entries and picks the strongest
// remaining one. ExposureKind is its own type (not the public ExposureType enum)
// because the wire shape allows None but the final FundCatalyst.exposure_type
// does not.
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
