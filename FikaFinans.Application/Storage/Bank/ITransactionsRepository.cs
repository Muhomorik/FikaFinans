using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="TransactionEntity"/>.
/// Partitioned by month (<c>"ledger/{yyyy-MM}"</c>), keyed within each
/// partition by the transaction Guid.
/// </summary>
public interface ITransactionsRepository
{
    Task<TransactionEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(TransactionEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<TransactionEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    /// <summary>
    /// Look up the (typically single) transaction that settled a given
    /// <c>TradingOrder</c>. The settle path uses this to find the
    /// reservation transaction that needs reversing.
    /// </summary>
    /// <remarks>
    /// Cross-partition scan in the worst case — implementations may
    /// narrow it to the partition derived from the order's submission
    /// date. Returns all matches; callers usually expect 0 or 1.
    /// </remarks>
    Task<IReadOnlyList<TransactionEntity>> GetByRelatedOrderAsync(Guid relatedOrderId, CancellationToken ct = default);

    /// <summary>
    /// Cross-partition scan returning every transaction header. Used by the
    /// bank UI's ledger view. Pair with
    /// <see cref="IJournalEntriesRepository.QueryByTransactionIdsAsync"/> to
    /// stitch entries onto each transaction.
    /// </summary>
    Task<IReadOnlyList<TransactionEntity>> QueryAllAsync(CancellationToken ct = default);
}
