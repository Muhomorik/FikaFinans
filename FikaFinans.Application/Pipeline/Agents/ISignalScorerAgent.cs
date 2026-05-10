using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface ISignalScorerAgent
{
    DataLoaderOutput Run(string isoWeek, string runId);
}
