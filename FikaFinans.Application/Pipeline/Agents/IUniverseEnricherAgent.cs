using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IUniverseEnricherAgent
{
    Task<DataLoaderOutput> RunAsync(string isoWeek, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Same processing as <see cref="RunAsync"/> but the Step 8 input is
    /// supplied by the caller instead of read from disk. The disk write of
    /// the Step 9 output stays for now — WPF still reads it (until
    /// Phase 8 sub-step 8c). Used by the streaming runner so the input
    /// path goes through the SQLite IsinProgress columns instead of the
    /// Step 8 disk file.
    /// </summary>
    Task<DataLoaderOutput> RunFromInputAsync(
        DataLoaderOutput step8Input,
        string isoWeek,
        PipelineRunId runId,
        CancellationToken ct = default);

    /// <summary>
    /// Enriches records already read — the whole universe in one pass. Storage-agnostic:
    /// reads nothing, writes nothing, so the caller owns both ends of the read-modify-write.
    /// Every input is a domain model on purpose: the enricher must not know whether the
    /// records came from a JSON file, progress rows or a queue message.
    /// </summary>
    /// <remarks>
    /// The four passes as in <see cref="RunAsync"/>, over <paramref name="funds"/> as the
    /// universe — pairs, peers and ranks are drawn from those records and no others. The
    /// rotation pair ids carry <paramref name="isoWeek"/>. The weights are checked here, so a
    /// bad config throws. Differentiator failures land in the output's
    /// <see cref="DataQuality.Warnings"/>. The portfolio-wide fields — frozen positions,
    /// cash — are left empty: no per-fund record carries them.
    /// </remarks>
    Task<DataLoaderOutput> RunInMemoryAsync(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds,
        UniverseEnricherConfig config,
        CancellationToken ct = default);
}
