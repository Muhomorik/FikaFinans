using System.Text.Json;

using FikaFinans.Application.Pipeline.Macro;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Macro;

/// <summary>
/// <see cref="IMacroContextStore"/> backed by <see cref="IMacroContextRepository"/>. The
/// storage backend swaps underneath it — the app's database locally, a partition of the
/// backend's Azure table in cloud — without this class changing.
/// </summary>
/// <remarks>
/// The context is stored in the same JSON shape as step 3's output file, so a stored row
/// and a <c>03-macro-*.json</c> file read the same.
/// </remarks>
public sealed class RepositoryBackedMacroContextStore : IMacroContextStore
{
    private const string MacroContextPartition = "macro-contexts";

    private readonly ILogger _logger;
    private readonly IMacroContextRepository _contexts;

    public RepositoryBackedMacroContextStore(ILogger logger, IMacroContextRepository contexts)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    }

    /// <inheritdoc />
    public async Task<MacroContext?> GetAsync(string weeklySummaryRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weeklySummaryRunId);

        var entity = await _contexts
            .GetAsync(MacroContextPartition, weeklySummaryRunId, ct)
            .ConfigureAwait(false);

        _logger.Trace(
            "Macro context lookup — weekly summary run {0}: {1}",
            weeklySummaryRunId, entity is null ? "miss" : $"hit, week {entity.IsoWeek}");

        return entity is null ? null : Deserialize(entity);
    }

    /// <inheritdoc />
    public async Task<MacroContext?> GetNewestAsync(IsoWeek notAfter, CancellationToken ct = default)
    {
        // The partition holds one row per published week — small enough to order here.
        // ISO week labels are zero-padded, so ordinal order is week order.
        var all = await _contexts
            .QueryPartitionAsync(MacroContextPartition, ct)
            .ConfigureAwait(false);

        var newest = all
            .Where(e => string.CompareOrdinal(e.IsoWeek, notAfter.Value) <= 0)
            .OrderByDescending(e => e.IsoWeek, StringComparer.Ordinal)
            .ThenByDescending(e => e.GeneratedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        _logger.Trace(
            "Macro context newest lookup — not after {0}: {1}",
            notAfter, newest is null ? "none" : $"week {newest.IsoWeek}, weekly summary run {newest.RowKey}");

        return newest is null ? null : Deserialize(newest);
    }

    /// <inheritdoc />
    public async Task SaveAsync(MacroContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entity = new MacroContextEntity
        {
            PartitionKey = MacroContextPartition,
            RowKey = context.SourceRunIds.WeeklySummaryRunId,
            IsoWeek = context.IsoWeek,
            GeneratedAt = context.GeneratedAt,
            ContextJson = JsonSerializer.Serialize(context, JsonOptions.Default),
        };

        await _contexts.UpsertAsync(entity, ct).ConfigureAwait(false);

        _logger.Trace(
            "Macro context saved — week {0}, weekly summary run {1}", entity.IsoWeek, entity.RowKey);
    }

    private static MacroContext Deserialize(MacroContextEntity entity) =>
        JsonSerializer.Deserialize<MacroContext>(entity.ContextJson, JsonOptions.Default)
        ?? throw new InvalidDataException(
            $"Stored macro context is empty — weekly summary run {entity.RowKey}");
}
