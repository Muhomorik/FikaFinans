using FikaFinans.Domain.Portfolio;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IPortfolioConstructorAgent
{
    TradesOutput Run(string isoWeek, string runId, string? macroRegime = null);
}
