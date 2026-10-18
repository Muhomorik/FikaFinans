using FikaFinans.Domain.Funds;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IThesisValidatorAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button
    /// and the current sequential <c>PipelineRunner</c>. Reads step 06 output
    /// file, processes every fund, writes the output file, and returns the
    /// in-memory result.
    /// </summary>
    Task<DataLoaderOutput> RunAsync(string isoWeek, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Validates funds already read. Reads nothing, writes nothing, so the caller owns both
    /// ends of the read-modify-write. Every input is a domain model on purpose: the validator
    /// must not know whether the records came from a JSON file, a progress row or a queue
    /// message.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessFundAsync"/> per record, wrapped in an output stamped with the
    /// arguments; the per-fund warnings land in its <see cref="DataQuality.Warnings"/>. The
    /// portfolio-wide fields — frozen positions, cash — are left empty: they are not an input
    /// to this step. Calls the LLM only for funds on the matrix's conflicting rows.
    /// </remarks>
    Task<DataLoaderOutput> RunInMemoryAsync(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds,
        CancellationToken ct = default);

    /// <summary>
    /// Per-fund compute. Hybrid: deterministic baseline (signal × catalyst ×
    /// macro alignment), refined by an LLM rationale for borderline cases.
    /// Returns the enriched <see cref="FundRecord"/> together with any
    /// universe-level warnings produced for this fund (e.g. null signal,
    /// LLM-override clamping).
    /// </summary>
    Task<FundProcessingResult> ProcessFundAsync(FundRecord fund, CancellationToken ct = default);
}
