using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IRecommenderAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 07 output
    /// file, processes every fund, writes the output file, and returns the
    /// in-memory result.
    /// </summary>
    DataLoaderOutput Run(string isoWeek, string runId);

    /// <summary>
    /// Per-fund compute. Deterministic mapping from
    /// (signal, thesis, catalyst.exposure_type, currently_held) to
    /// <see cref="Domain.Portfolio.Recommendation"/>. Returns the enriched
    /// <see cref="FundRecord"/> together with any universe-level warnings
    /// produced for this fund (e.g. null signal).
    /// </summary>
    FundProcessingResult ProcessFund(FundRecord fund);
}
