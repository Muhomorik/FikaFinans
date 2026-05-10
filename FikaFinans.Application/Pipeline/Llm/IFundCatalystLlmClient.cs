using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Llm;

public interface IFundCatalystLlmClient
{
    Task<IReadOnlyList<CatalystExposureClassification>> ClassifyAsync(
        string fundName,
        string fundCategory,
        IReadOnlyList<Catalyst> activeCatalysts,
        CancellationToken ct = default);
}
