using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface ISignalScorerAgent
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
    /// copy with <see cref="FundRecord.Signal"/>,
    /// <see cref="FundRecord.RuleFired"/>, and
    /// <see cref="FundRecord.CriteriaEvaluation"/> populated. All data-quality
    /// warnings live on <c>CriteriaEvaluation.DataQualityWarnings</c>, so
    /// nothing needs to leak out to the orchestrator.
    /// </summary>
    FundRecord ProcessFund(FundRecord fund, SignalScorerConfig config);
}
