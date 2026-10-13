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
    /// Scores funds already read. Pure and storage-agnostic — reads nothing, writes nothing,
    /// so the caller owns both ends of the read-modify-write. Every input is a domain model
    /// on purpose: the scorer must not know whether the records came from a JSON file, a
    /// progress row or a queue message.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessFund"/> per record, wrapped in an output stamped with the arguments.
    /// The portfolio-wide fields — frozen positions, cash — are left empty: they are not an
    /// input to this step.
    /// </remarks>
    DataLoaderOutput RunInMemory(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds,
        SignalScorerConfig config);

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
