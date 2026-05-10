using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IMetricsCalculatorAgent
{
    DataLoaderOutput Run(string isoWeek, string runId);
}
