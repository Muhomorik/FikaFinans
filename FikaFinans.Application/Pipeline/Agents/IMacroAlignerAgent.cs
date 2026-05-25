using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IMacroAlignerAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 04 + 03
    /// output files, processes every fund, writes the output file, and
    /// returns the in-memory result.
    /// </summary>
    Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default);

    /// <summary>
    /// Per-fund compute. Hybrid: direct category match (deterministic) plus a
    /// lazy LLM adjacency call when needed. Returns the enriched
    /// <see cref="FundRecord"/> together with any universe-level warnings
    /// produced for this fund (e.g. null category, unknown LLM theme id) so
    /// the orchestrator can fold them into
    /// <see cref="DataQuality.Warnings"/>.
    /// </summary>
    Task<FundProcessingResult> ProcessFundAsync(
        FundRecord fund,
        IReadOnlyList<RotationTheme> activeThemes,
        CancellationToken ct = default);
}
