using FikaFinans.Domain.Funds;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IDataLoaderAgent
{
    DataLoaderOutput Run(string family, string isoWeek, PipelineRunId runId);
}
