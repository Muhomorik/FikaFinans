using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Storage.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IPositionsRepository"/>. Pure POCO ↔ row
/// mapping — no domain types involved. Each call opens a fresh
/// short-lived <see cref="BankDbContext"/> via the factory and disposes
/// it on the way out; tracking never crosses method boundaries.
/// </summary>
public sealed class SqlitePositionsRepository : IPositionsRepository
{
    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqlitePositionsRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<PositionEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Positions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartitionKey == partitionKey && p.RowKey == rowKey, ct);
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<PositionEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Positions.AsNoTracking()
            .Where(p => p.PartitionKey == partitionKey)
            .ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(PositionEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<PositionEntity> entities, CancellationToken ct = default)
    {
        TableBatchAsserts.EnsureSinglePartitionBatch(partitionKey, entities);
        await using var db = await _factory.CreateDbContextAsync(ct);
        foreach (var entity in entities)
            await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Positions
            .Where(p => p.PartitionKey == partitionKey && p.RowKey == rowKey)
            .ExecuteDeleteAsync(ct);
    }

    private static async Task UpsertCoreAsync(BankDbContext db, PositionEntity entity, CancellationToken ct)
    {
        var existing = await db.Positions
            .FirstOrDefaultAsync(p => p.PartitionKey == entity.PartitionKey && p.RowKey == entity.RowKey, ct);

        if (existing is null)
        {
            db.Positions.Add(ToRow(entity));
        }
        else
        {
            existing.Isin = entity.Isin;
            existing.Name = entity.Name;
            existing.CurrentValueKr = entity.CurrentValueKr;
            existing.CostBasisKr = entity.CostBasisKr;
            existing.Units = entity.Units;
            existing.AvgCostPerUnit = entity.AvgCostPerUnit;
            existing.LastUpdatedAt = entity.LastUpdatedAt;
            existing.Source = entity.Source;
        }
    }

    private static PositionEntity ToEntity(PositionRow r) => new()
    {
        PartitionKey = r.PartitionKey,
        RowKey = r.RowKey,
        Isin = r.Isin,
        Name = r.Name,
        CurrentValueKr = r.CurrentValueKr,
        CostBasisKr = r.CostBasisKr,
        Units = r.Units,
        AvgCostPerUnit = r.AvgCostPerUnit,
        LastUpdatedAt = r.LastUpdatedAt,
        Source = r.Source
    };

    private static PositionRow ToRow(PositionEntity e) => new()
    {
        PartitionKey = e.PartitionKey,
        RowKey = e.RowKey,
        Isin = e.Isin,
        Name = e.Name,
        CurrentValueKr = e.CurrentValueKr,
        CostBasisKr = e.CostBasisKr,
        Units = e.Units,
        AvgCostPerUnit = e.AvgCostPerUnit,
        LastUpdatedAt = e.LastUpdatedAt,
        Source = e.Source
    };
}
