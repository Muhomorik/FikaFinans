using System.Diagnostics;

using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Identifiers;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Progress;

/// <summary>
/// <see cref="IIsinProgressClaim"/> backed by <see cref="IIsinProgressRepository"/>.
/// The storage backend swaps underneath it — SQLite today, the in-memory double in
/// tests, Azure Tables once the cloud store lands — without this class changing.
/// </summary>
/// <remarks>
/// Read-then-write, not a compare-and-swap: <c>TableEntity</c> carries no ETag, so two
/// claims arriving between the read and the write both succeed and the later one wins.
/// That is enough for one process reading one signal stream, which is all that runs
/// today; a second writer needs a real conditional update on the store.
/// </remarks>
public sealed class RepositoryBackedIsinProgressClaim : IIsinProgressClaim
{
    private const string IsinProgressPartition = "isin-progress";

    /// <summary>Step 1 is the first step of the per-ISIN chain.</summary>
    private const int FirstStep = 1;

    private readonly ILogger _logger;
    private readonly IIsinProgressRepository _isinProgress;

    public RepositoryBackedIsinProgressClaim(ILogger logger, IIsinProgressRepository isinProgress)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isinProgress = isinProgress ?? throw new ArgumentNullException(nameof(isinProgress));
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(Isin isin, DateTimeOffset navDate, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var existing = await _isinProgress
            .GetAsync(IsinProgressPartition, isin.Value, ct)
            .ConfigureAwait(false);

        if (existing?.State == IsinProgressState.Processing)
        {
            _logger.Trace(
                "Progress claim refused — isin={0} in flight since {1:u}, runId={2}",
                isin.Value, existing.ProcessingStartedAt, existing.RunId?.Value);
            return false;
        }

        // A fresh entity, not a copy: the repository replaces every column, so the
        // columns left unset here are the previous run's outputs being cleared.
        var claimed = new IsinProgressEntity
        {
            PartitionKey = IsinProgressPartition,
            RowKey = isin.Value,
            Isin = isin.Value,
            State = IsinProgressState.Processing,
            // The run id is minted a phase later, once the fund loads.
            RunId = null,
            NavDate = navDate,
            CurrentStep = FirstStep,
            // The one column carried across the run boundary: wiping it would re-process
            // every trading date the fund has already been through.
            LatestProcessedNavDate = existing?.LatestProcessedNavDate,
            ProcessingStartedAt = DateTimeOffset.UtcNow,
        };

        await _isinProgress.UpsertAsync(claimed, ct).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.Trace(
            "Progress claim done — isin={0}, navDate={1:yyyy-MM-dd}, {2} ms",
            isin.Value, navDate, stopwatch.ElapsedMilliseconds);

        return true;
    }
}
