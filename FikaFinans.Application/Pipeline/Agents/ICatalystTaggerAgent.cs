using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Agents;

public interface ICatalystTaggerAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 05 + 03
    /// output files, processes every fund, writes the output file, and
    /// returns the in-memory result.
    /// </summary>
    Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default);

    /// <summary>
    /// Per-fund compute. LLM classification of fund-vs-catalyst exposure,
    /// short-circuited when the active-catalyst list is empty or the fund has
    /// no category. Returns the enriched <see cref="FundRecord"/> together
    /// with any universe-level warnings produced for this fund.
    /// </summary>
    Task<FundProcessingResult> ProcessFundAsync(
        FundRecord fund,
        IReadOnlyList<Catalyst> activeCatalysts,
        CancellationToken ct = default);
}
