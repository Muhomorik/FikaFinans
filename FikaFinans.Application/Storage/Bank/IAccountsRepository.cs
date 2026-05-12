using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="AccountEntity"/>. All accounts
/// live in the single <c>"accounts"</c> partition keyed by account
/// <c>Code</c>.
/// </summary>
public interface IAccountsRepository
{
    Task<AccountEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<AccountEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(AccountEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<AccountEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    /// <summary>
    /// Account-by-code lookup. Codes are unique within the single
    /// <c>"accounts"</c> partition, so this is effectively a point read
    /// by RowKey.
    /// </summary>
    Task<AccountEntity?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Account-by-id lookup. Account Guids aren't part of the Tables key
    /// (RowKey is Code), so this is a partition scan in Tables-land. Used
    /// by ledger code that holds an <c>AccountId</c> from journal-entry
    /// rows and needs the account's type/currency to compute a balance.
    /// </summary>
    Task<AccountEntity?> GetByIdAsync(Guid accountId, CancellationToken ct = default);
}
