using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="TradingOrderEntity"/>. Orders
/// are partitioned by creation date (<c>"orders/{yyyy-MM-dd}"</c>); RowKey
/// is <c>"{isin}/{side}"</c> so a re-submitted order on the same day
/// overwrites the first.
/// </summary>
public interface ITradingOrdersRepository
{
    Task<TradingOrderEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<TradingOrderEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(TradingOrderEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<TradingOrderEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    /// <summary>
    /// Cross-partition scan returning every order whose <c>Status</c> matches
    /// (case-sensitive). Used by the settlement engine to drain pending orders
    /// and by the bank UI to populate the "pending" view.
    /// </summary>
    /// <remarks>
    /// In Tables-land this is a partition-by-partition scan. Acceptable for
    /// the bank-sim because the hot set ("Pending") is small.
    /// </remarks>
    Task<IReadOnlyList<TradingOrderEntity>> QueryByStatusAsync(string status, CancellationToken ct = default);

    /// <summary>
    /// Cross-partition scan returning every order, newest first. Used by the
    /// bank UI's "all orders" history view.
    /// </summary>
    Task<IReadOnlyList<TradingOrderEntity>> QueryAllAsync(CancellationToken ct = default);
}
