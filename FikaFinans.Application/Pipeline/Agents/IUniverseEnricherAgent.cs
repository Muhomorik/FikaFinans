using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IUniverseEnricherAgent
{
    Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default);

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
        string runId,
        CancellationToken ct = default);
}
