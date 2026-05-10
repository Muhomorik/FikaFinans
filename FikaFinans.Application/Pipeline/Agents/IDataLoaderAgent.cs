using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IDataLoaderAgent
{
    DataLoaderOutput Run(string family, string isoWeek, string runId);
}
