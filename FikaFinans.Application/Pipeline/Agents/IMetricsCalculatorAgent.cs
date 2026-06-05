using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IMetricsCalculatorAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads its input
    /// from the prior step's JSON file, processes every fund, writes the
    /// output file, and returns the in-memory result.
    /// </summary>
    DataLoaderOutput Run(string isoWeek, PipelineRunId runId);

    /// <summary>
    /// Per-fund compute. Pure function over a single
    /// <see cref="FundRecord"/> and the agent's config; returns an enriched
    /// copy with <see cref="FundRecord.Metrics"/> populated. The per-ISIN
    /// streaming path in the runner calls this directly, one fund at a
    /// time, so the agent can participate in the
    /// <c>Merge(maxConcurrent: N)</c> stream without each call touching
    /// disk or rebuilding the universe.
    /// </summary>
    FundRecord ProcessFund(FundRecord fund, MetricsCalculatorConfig config);
}
