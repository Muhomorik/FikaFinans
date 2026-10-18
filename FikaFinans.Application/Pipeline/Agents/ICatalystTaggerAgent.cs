using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface ICatalystTaggerAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 05 + 03
    /// output files, processes every fund, writes the output file, and
    /// returns the in-memory result.
    /// </summary>
    Task<DataLoaderOutput> RunAsync(string isoWeek, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Tags funds already read against catalysts already read. Reads nothing, writes nothing,
    /// so the caller owns both ends of the read-modify-write. Every input is a domain model
    /// on purpose: the tagger must not know whether the records came from a JSON file, a
    /// progress row or a queue message — nor where the catalysts were stored.
    /// </summary>
    /// <remarks>
    /// Catalysts with no affected categories are skipped, as the contract asks, so the caller
    /// passes the list as stored. Then <see cref="ProcessFundAsync"/> per record, wrapped in an
    /// output stamped with the arguments; the per-fund warnings land in its
    /// <see cref="DataQuality.Warnings"/>. The portfolio-wide fields — frozen positions, cash —
    /// are left empty: they are not an input to this step. Calls the LLM once per fund with a
    /// category, whenever a catalyst is left.
    /// </remarks>
    Task<DataLoaderOutput> RunInMemoryAsync(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds,
        IReadOnlyList<Catalyst> catalysts,
        CancellationToken ct = default);

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
