using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Storage.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IMacroContextRepository"/>. Pure POCO ↔ row mapping. Each call
/// opens a fresh short-lived <see cref="BankDbContext"/> via the factory and disposes it on
/// the way out; tracking never crosses method boundaries.
/// </summary>
public sealed class SqliteMacroContextRepository : IMacroContextRepository
{
    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteMacroContextRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<MacroContextEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MacroContexts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartitionKey == partitionKey && p.RowKey == rowKey, ct);
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<MacroContextEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.MacroContexts.AsNoTracking()
            .Where(p => p.PartitionKey == partitionKey)
            .ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(MacroContextEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.MacroContexts
            .FirstOrDefaultAsync(p => p.PartitionKey == entity.PartitionKey && p.RowKey == entity.RowKey, ct);

        if (existing is null)
        {
            var row = new MacroContextRow { PartitionKey = entity.PartitionKey, RowKey = entity.RowKey };
            CopyInto(row, entity);
            db.MacroContexts.Add(row);
        }
        else
        {
            CopyInto(existing, entity);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void CopyInto(MacroContextRow row, MacroContextEntity e)
    {
        row.IsoWeek = e.IsoWeek;
        row.GeneratedAt = e.GeneratedAt;
        row.ContextJson = e.ContextJson;
    }

    private static MacroContextEntity ToEntity(MacroContextRow r) => new()
    {
        PartitionKey = r.PartitionKey,
        RowKey = r.RowKey,
        IsoWeek = r.IsoWeek,
        GeneratedAt = r.GeneratedAt,
        ContextJson = r.ContextJson,
    };
}
