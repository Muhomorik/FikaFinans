using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="MacroContextEntity"/>. Every row lives in the
/// single <c>"macro-contexts"</c> partition keyed by the weekly summary's <c>runId</c>.
/// </summary>
public interface IMacroContextRepository
{
    Task<MacroContextEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<MacroContextEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(MacroContextEntity entity, CancellationToken ct = default);
}
