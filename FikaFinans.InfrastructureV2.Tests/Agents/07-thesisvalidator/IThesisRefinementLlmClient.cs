using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.ThesisValidator;

namespace FikaFinans.InfrastructureV2.Tests.Agents.ThesisValidator;

public interface IThesisRefinementLlmClient
{
    Task<ThesisRefinementVerdict> RefineAsync(
        FundRecord fund,
        ThesisValidity baseline,
        CancellationToken ct = default);
}
