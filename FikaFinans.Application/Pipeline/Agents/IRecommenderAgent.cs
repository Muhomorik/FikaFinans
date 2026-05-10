using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IRecommenderAgent
{
    DataLoaderOutput Run(string isoWeek, string runId);
}
