namespace FikaFinans.Application.Pipeline.Llm;

public interface IDifferentiatorLlmClient
{
    Task<IReadOnlyList<DifferentiatorLine>> WriteDifferentiatorsAsync(
        DifferentiatorRequest request,
        CancellationToken ct = default);
}
