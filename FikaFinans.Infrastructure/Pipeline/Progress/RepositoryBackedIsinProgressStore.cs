using System.Diagnostics;
using System.Text.Json;

using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Progress;

/// <summary>
/// <see cref="IIsinProgressStore"/> backed by <see cref="IIsinProgressRepository"/>.
/// The storage backend swaps underneath it — SQLite today, the in-memory double in
/// tests, Azure Tables once the cloud store lands — without this class changing.
/// </summary>
/// <remarks>
/// Read-then-write, not a compare-and-swap: <c>TableEntity</c> carries no ETag, so two
/// claims arriving between the read and the write both succeed and the later one wins.
/// That is enough for one process reading one signal stream, which is all that runs
/// today; a second writer needs a real conditional update on the store.
/// </remarks>
public sealed class RepositoryBackedIsinProgressStore : IIsinProgressStore
{
    private const string IsinProgressPartition = "isin-progress";

    private readonly ILogger _logger;
    private readonly IIsinProgressRepository _isinProgress;

    public RepositoryBackedIsinProgressStore(ILogger logger, IIsinProgressRepository isinProgress)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isinProgress = isinProgress ?? throw new ArgumentNullException(nameof(isinProgress));
    }

    /// <inheritdoc />
    public async Task<IsinProgressEntity?> ClaimAsync(
        Isin isin, DateTimeOffset navDate, CancellationToken ct = default)
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
            return null;
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
            CurrentStep = StepId.DataLoader.Value,
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

        return claimed;
    }

    /// <inheritdoc />
    public async Task<IsinProgressEntity> SaveStepOutputAsync(
        Isin isin, StepId step, PipelineRunId runId, DataLoaderOutput output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(runId.Value);

        var stopwatch = Stopwatch.StartNew();

        var existing = await _isinProgress
            .GetAsync(IsinProgressPartition, isin.Value, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No progress row for ISIN {isin.Value} — the step wrote output without claiming the row first.");

        // One fund per column: the row is this ISIN's, so the records the agent produced
        // for anything else belong on their own rows. A fund the agent dropped — an
        // unknown ISIN, or one outside the company filter — leaves the column null.
        var record = output.Funds.FirstOrDefault(f => f.Isin == isin);

        if (record is null)
            _logger.Info("Progress output empty — isin={0} produced no record at {1}", isin.Value, step);

        var json = record is null ? null : JsonSerializer.Serialize(record, JsonOptions.Default);
        var written = WithStepOutput(existing, step, runId, json);

        await _isinProgress.UpsertAsync(written, ct).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.Trace(
            "Progress output done — isin={0}, {1}, runId={2}, {3} chars, {4} ms",
            isin.Value, step, runId.Value, json?.Length ?? 0, stopwatch.ElapsedMilliseconds);

        return written;
    }

    /// <summary>
    /// Copies a row, replacing the one <c>Step{N}Json</c> column <paramref name="step"/>
    /// owns and advancing the run marker to it. Every other column survives untouched,
    /// which the full-row-replace upsert would otherwise drop.
    /// </summary>
    private static IsinProgressEntity WithStepOutput(
        IsinProgressEntity row, StepId step, PipelineRunId runId, string? json) => new()
    {
        PartitionKey = row.PartitionKey,
        RowKey = row.RowKey,
        Isin = row.Isin,
        State = row.State,
        RunId = runId,
        NavDate = row.NavDate,
        CurrentStep = step.Value,
        LatestProcessedNavDate = row.LatestProcessedNavDate,
        ProcessingStartedAt = row.ProcessingStartedAt,
        LastError = row.LastError,
        AttemptCount = row.AttemptCount,
        Step01Json = step.Value == 1 ? json : row.Step01Json,
        Step02Json = step.Value == 2 ? json : row.Step02Json,
        Step03Json = step.Value == 3 ? json : row.Step03Json,
        Step04Json = step.Value == 4 ? json : row.Step04Json,
        Step05Json = step.Value == 5 ? json : row.Step05Json,
        Step06Json = step.Value == 6 ? json : row.Step06Json,
        Step07Json = step.Value == 7 ? json : row.Step07Json,
        Step08Json = step.Value == 8 ? json : row.Step08Json,
        Step09Json = step.Value == 9 ? json : row.Step09Json,
    };
}
