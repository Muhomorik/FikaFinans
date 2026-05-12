using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="JournalEntryEntity"/>.
/// Entries share a partition with their parent <see cref="TransactionEntity"/>
/// — typically a month bucket. RowKey is the entry Guid.
/// </summary>
public interface IJournalEntriesRepository
{
    Task<JournalEntryEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntryEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(JournalEntryEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<JournalEntryEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    /// <summary>
    /// Return every entry that posts to <paramref name="accountId"/>,
    /// across all ledger partitions. Used by account-history reads.
    /// </summary>
    /// <remarks>
    /// Cross-partition scan — Tables semantics push this toward an
    /// indexed column rather than a partition-bound query. Acceptable
    /// today because the bank-sim only has a handful of months of
    /// ledger data.
    /// </remarks>
    Task<IReadOnlyList<JournalEntryEntity>> QueryByAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Stitching helper: fetch every entry whose <c>TransactionId</c> appears
    /// in the supplied set. Service-layer code passes the IDs of transactions
    /// it just read and groups the results by transaction ID in memory —
    /// this replaces the EF nav-prop <c>Include(t =&gt; t.Entries)</c> path.
    /// </summary>
    Task<IReadOnlyList<JournalEntryEntity>> QueryByTransactionIdsAsync(
        IReadOnlyList<Guid> transactionIds, CancellationToken ct = default);
}
