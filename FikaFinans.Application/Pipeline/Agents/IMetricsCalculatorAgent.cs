using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

/// <summary>
/// Step 2 of the pipeline (<see cref="StepId.MetricsCalculator"/>). Enriches each fund record
/// from step 1 with its <see cref="FundRecord.Metrics"/> — returns, Sharpe, volatility,
/// drawdown and data-quality flags. Pure code, no LLM.
/// </summary>
/// <remarks>
/// Append-only: every field step 1 produced is carried through untouched; only
/// <see cref="FundRecord.Metrics"/> is added.
/// </remarks>
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
    /// Computes metrics for funds already read. Pure and storage-agnostic — reads nothing,
    /// writes nothing, so the caller owns both ends of the read-modify-write. Every input is
    /// a domain model on purpose: the calculator must not know whether the records came from
    /// a JSON file, a progress row or a queue message.
    /// </summary>
    /// <remarks>
    /// The portfolio-wide fields of the returned output — frozen positions, cash — are left
    /// empty: they are not an input to this step.
    /// </remarks>
    DataLoaderOutput RunInMemory(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds,
        MetricsCalculatorConfig config);

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
