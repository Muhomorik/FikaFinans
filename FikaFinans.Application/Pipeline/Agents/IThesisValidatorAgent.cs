using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IThesisValidatorAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 06 output
    /// file, processes every fund, writes the output file, and returns the
    /// in-memory result.
    /// </summary>
    Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default);

    /// <summary>
    /// Per-fund compute. Hybrid: deterministic baseline (signal × catalyst ×
    /// macro alignment), refined by an LLM rationale for borderline cases.
    /// Returns the enriched <see cref="FundRecord"/> together with any
    /// universe-level warnings produced for this fund (e.g. null signal,
    /// LLM-override clamping).
    /// </summary>
    Task<FundProcessingResult> ProcessFundAsync(FundRecord fund, CancellationToken ct = default);
}
