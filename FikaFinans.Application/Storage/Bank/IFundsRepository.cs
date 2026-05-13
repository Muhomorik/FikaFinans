using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="FundEntity"/> and
/// <see cref="NavSnapshotEntity"/>. Funds live in the single
/// <c>"funds"</c> partition keyed by ISIN; each fund's NAV history lives
/// in <c>"nav/{isin}"</c> keyed by ISO 8601 timestamp. Retires the direct
/// EF reads of <c>Fund</c>/<c>NavSnapshot</c> from the bank-sim.
/// </summary>
public interface IFundsRepository
{
    Task<FundEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<FundEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(FundEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<FundEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<FundEntity?> GetByIsinAsync(Isin isin, CancellationToken ct = default);

    Task<FundEntity?> GetByIdAsync(FundId fundId, CancellationToken ct = default);

    Task<decimal?> GetLatestNavAsync(Isin isin, CancellationToken ct = default);

    Task<decimal?> GetLatestNavByFundIdAsync(FundId fundId, CancellationToken ct = default);

    Task<IReadOnlyList<NavSnapshotEntity>> QueryNavHistoryAsync(Isin isin, CancellationToken ct = default);

    Task UpsertNavAsync(NavSnapshotEntity nav, CancellationToken ct = default);
}
