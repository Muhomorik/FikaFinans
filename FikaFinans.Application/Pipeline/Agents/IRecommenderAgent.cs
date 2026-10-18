using FikaFinans.Domain.Funds;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IRecommenderAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 07 output
    /// file, processes every fund, writes the output file, and returns the
    /// in-memory result.
    /// </summary>
    DataLoaderOutput Run(string isoWeek, PipelineRunId runId);

    /// <summary>
    /// Recommends for funds already read. Pure and storage-agnostic — reads nothing, writes
    /// nothing, so the caller owns both ends of the read-modify-write. Every input is a domain
    /// model on purpose: the recommender must not know whether the records came from a JSON
    /// file, a progress row or a queue message.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessFund"/> per record, wrapped in an output stamped with the arguments;
    /// the per-fund warnings land in its <see cref="DataQuality.Warnings"/>. The portfolio-wide
    /// fields — frozen positions, cash — are left empty: no rule here reads them. Synchronous,
    /// as step 4's: the mapping awaits nothing.
    /// </remarks>
    DataLoaderOutput RunInMemory(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds);

    /// <summary>
    /// Per-fund compute. Deterministic mapping from
    /// (signal, thesis, catalyst.exposure_type, currently_held) to
    /// <see cref="Domain.Portfolio.Recommendation"/>. Returns the enriched
    /// <see cref="FundRecord"/> together with any universe-level warnings
    /// produced for this fund (e.g. null signal).
    /// </summary>
    FundProcessingResult ProcessFund(FundRecord fund);
}
