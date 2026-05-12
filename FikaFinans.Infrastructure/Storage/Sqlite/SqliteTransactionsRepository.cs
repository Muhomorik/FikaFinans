using System.Globalization;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Ledger;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using TransactionStatusEnum = FikaFinans.Domain.Bank.Ledger.TransactionStatus;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="ITransactionsRepository"/>. Fully functional —
/// chunk 4 lit up upsert by removing the <c>Transaction.Entries</c> cascade
/// and rehydrating the aggregate via a non-validating factory.
/// </summary>
/// <remarks>
/// PartitionKey shape <c>"ledger/{yyyy-MM}"</c> is reconstructed from
/// <see cref="Transaction.Timestamp"/>; RowKey is the
/// <see cref="TransactionId"/> Guid as a string.
/// </remarks>
public sealed class SqliteTransactionsRepository : ITransactionsRepository
{
    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteTransactionsRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<TransactionEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!TryParseLedgerPartition(partitionKey, out var monthStart, out var monthEnd))
            return null;
        if (!Guid.TryParse(rowKey, out var id))
            return null;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var txn = await db.Transactions.AsNoTracking()
            .Where(t => t.Id == new TransactionId(id)
                     && t.Timestamp >= monthStart
                     && t.Timestamp < monthEnd)
            .FirstOrDefaultAsync(ct);
        return txn is null ? null : ToEntity(txn);
    }

    public async Task<IReadOnlyList<TransactionEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        if (!TryParseLedgerPartition(partitionKey, out var monthStart, out var monthEnd))
            return Array.Empty<TransactionEntity>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Timestamp >= monthStart && t.Timestamp < monthEnd)
            .ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<TransactionEntity>> GetByRelatedOrderAsync(Guid relatedOrderId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var orderId = new TradingOrderId(relatedOrderId);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.RelatedOrderId == orderId)
            .ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(TransactionEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<TransactionEntity> entities, CancellationToken ct = default)
    {
        TableBatchAsserts.EnsureSinglePartitionBatch(partitionKey, entities);
        await using var db = await _factory.CreateDbContextAsync(ct);
        foreach (var entity in entities)
            await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TransactionEntity>> QueryAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Transactions.AsNoTracking().ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    private static async Task UpsertCoreAsync(BankDbContext db, TransactionEntity entity, CancellationToken ct)
    {
        var txnId = new TransactionId(entity.TransactionId);
        var existing = await db.Transactions.FirstOrDefaultAsync(t => t.Id == txnId, ct);
        var status = Enum.Parse<TransactionStatusEnum>(entity.Status);
        var relatedOrderId = entity.RelatedOrderId.HasValue
            ? new TradingOrderId(entity.RelatedOrderId.Value)
            : (TradingOrderId?)null;

        if (existing is null)
        {
            // Empty entries — JournalEntries are persisted independently
            // via SqliteJournalEntriesRepository; the EF model no longer
            // tracks the Entries collection (TransactionConfiguration: Ignore).
            var transaction = Transaction.Rehydrate(
                txnId,
                entity.Timestamp,
                entity.Description,
                status,
                relatedOrderId,
                Array.Empty<JournalEntry>());
            db.Transactions.Add(transaction);
        }
        else
        {
            // Last-write-wins: replace mutable header fields. Id stays
            // (it's the EF key); Entries are out-of-band, not touched here.
            db.Entry(existing).CurrentValues.SetValues(new
            {
                entity.Timestamp,
                entity.Description,
                Status = status,
                RelatedOrderId = relatedOrderId
            });
        }
    }

    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!TryParseLedgerPartition(partitionKey, out var monthStart, out var monthEnd))
            return;
        if (!Guid.TryParse(rowKey, out var id))
            return;

        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Transactions
            .Where(t => t.Id == new TransactionId(id)
                     && t.Timestamp >= monthStart
                     && t.Timestamp < monthEnd)
            .ExecuteDeleteAsync(ct);
    }

    private static TransactionEntity ToEntity(Transaction t) => new()
    {
        PartitionKey = $"ledger/{t.Timestamp:yyyy-MM}",
        RowKey = t.Id.Value.ToString(),
        TransactionId = t.Id.Value,
        Timestamp = t.Timestamp,
        Description = t.Description,
        Status = t.Status.ToString(),
        RelatedOrderId = t.RelatedOrderId?.Value
    };

    private static bool TryParseLedgerPartition(string partitionKey, out DateTimeOffset monthStart, out DateTimeOffset monthEnd)
    {
        monthStart = default;
        monthEnd = default;
        const string prefix = "ledger/";
        if (!partitionKey.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var monthPart = partitionKey[prefix.Length..];
        if (!DateTime.TryParseExact(monthPart, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        monthStart = new DateTimeOffset(parsed.Year, parsed.Month, 1, 0, 0, 0, TimeSpan.Zero);
        monthEnd = monthStart.AddMonths(1);
        return true;
    }
}
