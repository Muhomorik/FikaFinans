using FikaFinans.Application.Agents;
using FikaFinans.Domain.Models;

namespace FikaFinans.Application.UseCases;

/// <summary>
/// Runs one question through all registered fund-analytics agents in parallel.
/// Per-model failures don't abort sibling runs — each <see cref="FundAnalyticsRun"/>
/// stands on its own.
/// </summary>
public sealed class CompareModelsUseCase
{
    private readonly IReadOnlyList<IFundAnalyticsAgent> _agents;

    public CompareModelsUseCase(IEnumerable<IFundAnalyticsAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents.ToList();
        if (_agents.Count == 0)
            throw new InvalidOperationException("No IFundAnalyticsAgent registrations found");
    }

    public async Task<ModelComparison> ExecuteAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var tasks = _agents.Select(agent => agent.RunAsync(question, ct)).ToArray();
        var runs = await Task.WhenAll(tasks);
        return new ModelComparison(question, runs);
    }
}
