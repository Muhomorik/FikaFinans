using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Storage.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IIsinProgressRepository"/>. Pure POCO ↔ row
/// mapping with a string-converted <see cref="IsinProgressState"/> column
/// for Tables wire compatibility. Each call opens a fresh short-lived
/// <see cref="BankDbContext"/> via the factory and disposes it on the way
/// out; tracking never crosses method boundaries.
/// </summary>
public sealed class SqliteIsinProgressRepository : IIsinProgressRepository
{
    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteIsinProgressRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IsinProgressEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.IsinProgresses.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartitionKey == partitionKey && p.RowKey == rowKey, ct);
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<IsinProgressEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.IsinProgresses.AsNoTracking()
            .Where(p => p.PartitionKey == partitionKey)
            .ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(IsinProgressEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<IsinProgressEntity> entities, CancellationToken ct = default)
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
        await db.IsinProgresses
            .Where(p => p.PartitionKey == partitionKey && p.RowKey == rowKey)
            .ExecuteDeleteAsync(ct);
    }

    private static async Task UpsertCoreAsync(BankDbContext db, IsinProgressEntity entity, CancellationToken ct)
    {
        var existing = await db.IsinProgresses
            .FirstOrDefaultAsync(p => p.PartitionKey == entity.PartitionKey && p.RowKey == entity.RowKey, ct);

        if (existing is null)
        {
            db.IsinProgresses.Add(ToRow(entity));
        }
        else
        {
            CopyInto(existing, entity);
        }
    }

    private static void CopyInto(IsinProgressRow row, IsinProgressEntity e)
    {
        row.Isin = e.Isin;
        row.State = e.State.ToString();
        row.RunId = e.RunId;
        row.NavDate = e.NavDate;
        row.CurrentStep = e.CurrentStep;
        row.LatestProcessedNavDate = e.LatestProcessedNavDate;
        row.ProcessingStartedAt = e.ProcessingStartedAt;
        row.LastError = e.LastError;
        row.AttemptCount = e.AttemptCount;
        row.Step01Json = e.Step01Json;
        row.Step02Json = e.Step02Json;
        row.Step03Json = e.Step03Json;
        row.Step04Json = e.Step04Json;
        row.Step05Json = e.Step05Json;
        row.Step06Json = e.Step06Json;
        row.Step07Json = e.Step07Json;
        row.Step08Json = e.Step08Json;
        row.Step09Json = e.Step09Json;
    }

    private static IsinProgressEntity ToEntity(IsinProgressRow r) => new()
    {
        PartitionKey = r.PartitionKey,
        RowKey = r.RowKey,
        Isin = r.Isin,
        State = ParseState(r.State),
        RunId = r.RunId,
        NavDate = r.NavDate,
        CurrentStep = r.CurrentStep,
        LatestProcessedNavDate = r.LatestProcessedNavDate,
        ProcessingStartedAt = r.ProcessingStartedAt,
        LastError = r.LastError,
        AttemptCount = r.AttemptCount,
        Step01Json = r.Step01Json,
        Step02Json = r.Step02Json,
        Step03Json = r.Step03Json,
        Step04Json = r.Step04Json,
        Step05Json = r.Step05Json,
        Step06Json = r.Step06Json,
        Step07Json = r.Step07Json,
        Step08Json = r.Step08Json,
        Step09Json = r.Step09Json,
    };

    private static IsinProgressRow ToRow(IsinProgressEntity e)
    {
        var row = new IsinProgressRow
        {
            PartitionKey = e.PartitionKey,
            RowKey = e.RowKey,
        };
        CopyInto(row, e);
        return row;
    }

    private static IsinProgressState ParseState(string raw) =>
        Enum.TryParse<IsinProgressState>(raw, ignoreCase: false, out var v)
            ? v
            : throw new InvalidOperationException(
                $"Unknown IsinProgressState value '{raw}' read from storage.");
}
