using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

public interface IFundCatalystLlmClient
{
    // Returns one classification per active catalyst, in input order. Failures
    // (invalid JSON, hallucinated catalyst names, network blips) collapse to an
    // empty list — the agent treats that as "no catalyst applies".
    Task<IReadOnlyList<CatalystExposureClassification>> ClassifyAsync(
        string fundName,
        string fundCategory,
        IReadOnlyList<Catalyst> activeCatalysts,
        CancellationToken ct = default);
}
