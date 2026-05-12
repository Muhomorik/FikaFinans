using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Tables-shaped repository over <see cref="PositionEntity"/>. All rows
/// live in the single <c>"positions"</c> partition keyed by ISIN (or the
/// literal <c>"CASH"</c> for the cash pseudo-row). Replaces the
/// <c>FundHolding</c> EF entity and <c>positions.csv</c> from chunk 5
/// onward.
/// </summary>
public interface IPositionsRepository
{
    Task<PositionEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<PositionEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default);

    Task UpsertAsync(PositionEntity entity, CancellationToken ct = default);

    Task UpsertBatchAsync(string partitionKey, IReadOnlyList<PositionEntity> entities, CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);
}
