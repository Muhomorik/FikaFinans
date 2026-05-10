using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Llm;

public interface IThesisRefinementLlmClient
{
    Task<ThesisRefinementVerdict> RefineAsync(
        FundRecord fund,
        ThesisValidity baseline,
        CancellationToken ct = default);
}
