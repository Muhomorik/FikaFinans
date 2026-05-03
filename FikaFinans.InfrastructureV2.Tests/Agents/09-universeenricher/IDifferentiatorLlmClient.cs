namespace FikaFinans.InfrastructureV2.Tests.Agents.UniverseEnricher;

public interface IDifferentiatorLlmClient
{
    Task<IReadOnlyList<DifferentiatorLine>> WriteDifferentiatorsAsync(
        DifferentiatorRequest request,
        CancellationToken ct = default);
}
