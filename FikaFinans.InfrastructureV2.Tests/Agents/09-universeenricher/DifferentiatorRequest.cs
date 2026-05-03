using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Agents.UniverseEnricher;

// One Buy candidate plus its short list of peer alternatives. The LLM is
// invoked once per Buy to write all differentiators in a single call (cheaper
// and gives the model the full peer context it needs to compare).
public sealed class DifferentiatorRequest
{
    public required FundRecord Primary { get; init; }
    public required IReadOnlyList<FundRecord> Alternatives { get; init; }
}
