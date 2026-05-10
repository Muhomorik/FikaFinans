using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IThesisValidatorAgent
{
    Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default);
}
