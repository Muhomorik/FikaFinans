using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="IsinProgressEntity"/>. Every
/// row lives in the single <c>"isin-progress"</c> partition keyed by ISIN.
/// Backs the per-ISIN in-flight lock and the inline step-output store
/// described in
/// <see href="../../../../Docs/backend-nav-sync-plan.md">backend-nav-sync-plan.md</see>
/// §"Progress Table" + §"Step Outputs".
/// </summary>
public interface IIsinProgressRepository
{
    Task<IsinProgressEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<IsinProgressEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(IsinProgressEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<IsinProgressEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);
}
