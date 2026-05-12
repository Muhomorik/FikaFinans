using System.Globalization;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Ledger;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IJournalEntriesRepository"/>. Fully functional —
/// chunk 4 lit up upsert when JournalEntry was promoted to a top-level
/// DbSet and rehydrated via a non-validating factory.
/// </summary>
/// <remarks>
/// PartitionKey for a journal entry is its parent
/// <see cref="Transaction"/>'s partition (<c>"ledger/{yyyy-MM}"</c> from
/// the parent's <c>Timestamp</c>). RowKey is the
/// <see cref="JournalEntryId"/> Guid as a string. Reads stitch the
/// timestamp from the parent transaction in memory because the entry
/// row itself doesn't carry it.
/// </remarks>
public sealed class SqliteJournalEntriesRepository : IJournalEntriesRepository
{
    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteJournalEntriesRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<JournalEntryEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!Guid.TryParse(rowKey, out var id)) return null;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var entryId = new JournalEntryId(id);
        var entry = await db.JournalEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null) return null;

        var parentTimestamp = await db.Transactions.AsNoTracking()
            .Where(t => t.Id == entry.TransactionId)
            .Select(t => (DateTimeOffset?)t.Timestamp)
            .FirstOrDefaultAsync(ct);
        if (parentTimestamp is null) return null;

        var dto = ToEntity(entry, parentTimestamp.Value);
        return string.Equals(dto.PartitionKey, partitionKey, StringComparison.Ordinal) ? dto : null;
    }

    public async Task<IReadOnlyList<JournalEntryEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        if (!TryParseLedgerPartition(partitionKey, out var monthStart, out var monthEnd))
            return Array.Empty<JournalEntryEntity>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var monthTxns = await db.Transactions.AsNoTracking()
            .Where(t => t.Timestamp >= monthStart && t.Timestamp < monthEnd)
            .Select(t => new { t.Id, t.Timestamp })
            .ToListAsync(ct);
        if (monthTxns.Count == 0) return Array.Empty<JournalEntryEntity>();

        var txnIds = monthTxns.Select(t => t.Id).ToList();
        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => txnIds.Contains(e.TransactionId))
            .ToListAsync(ct);
        var timestampByTxnId = monthTxns.ToDictionary(t => t.Id, t => t.Timestamp);

        return entries.Select(e => ToEntity(e, timestampByTxnId[e.TransactionId])).ToList();
    }

    public async Task<IReadOnlyList<JournalEntryEntity>> QueryByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var typedAccountId = new AccountId(accountId);
        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.AccountId == typedAccountId)
            .ToListAsync(ct);
        if (entries.Count == 0) return Array.Empty<JournalEntryEntity>();

        var txnIds = entries.Select(e => e.TransactionId).Distinct().ToList();
        var txns = await db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Timestamp })
            .ToListAsync(ct);
        var timestampByTxnId = txns.ToDictionary(t => t.Id, t => t.Timestamp);

        return entries
            .Where(e => timestampByTxnId.ContainsKey(e.TransactionId))
            .Select(e => ToEntity(e, timestampByTxnId[e.TransactionId]))
            .ToList();
    }

    public async Task UpsertAsync(JournalEntryEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<JournalEntryEntity> entities, CancellationToken ct = default)
    {
        TableBatchAsserts.EnsureSinglePartitionBatch(partitionKey, entities);
        await using var db = await _factory.CreateDbContextAsync(ct);
        foreach (var entity in entities)
            await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JournalEntryEntity>> QueryByTransactionIdsAsync(
        IReadOnlyList<Guid> transactionIds, CancellationToken ct = default)
    {
        if (transactionIds is null || transactionIds.Count == 0)
            return Array.Empty<JournalEntryEntity>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var typedIds = transactionIds.Select(id => new TransactionId(id)).ToList();
        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => typedIds.Contains(e.TransactionId))
            .ToListAsync(ct);
        if (entries.Count == 0) return Array.Empty<JournalEntryEntity>();

        var distinctIds = entries.Select(e => e.TransactionId).Distinct().ToList();
        var txns = await db.Transactions.AsNoTracking()
            .Where(t => distinctIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Timestamp })
            .ToListAsync(ct);
        var timestampByTxnId = txns.ToDictionary(t => t.Id, t => t.Timestamp);

        return entries
            .Where(e => timestampByTxnId.ContainsKey(e.TransactionId))
            .Select(e => ToEntity(e, timestampByTxnId[e.TransactionId]))
            .ToList();
    }

    private static async Task UpsertCoreAsync(BankDbContext db, JournalEntryEntity entity, CancellationToken ct)
    {
        var entryId = new JournalEntryId(entity.JournalEntryId);
        var existing = await db.JournalEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);

        if (existing is null)
        {
            var entry = JournalEntry.Rehydrate(
                entryId,
                new TransactionId(entity.TransactionId),
                new AccountId(entity.AccountId),
                entity.DebitAmount,
                entity.CreditAmount,
                entity.Currency);
            db.JournalEntries.Add(entry);
        }
        else
        {
            // Last-write-wins on the mutable amounts. Identity (Id,
            // TransactionId, AccountId) shouldn't drift on an update
            // — but if it does, mirror the row exactly.
            db.Entry(existing).CurrentValues.SetValues(new
            {
                TransactionId = new TransactionId(entity.TransactionId),
                AccountId = new AccountId(entity.AccountId),
                entity.DebitAmount,
                entity.CreditAmount,
                entity.Currency
            });
        }
    }

    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!Guid.TryParse(rowKey, out var id)) return;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var entryId = new JournalEntryId(id);
        await db.JournalEntries
            .Where(e => e.Id == entryId)
            .ExecuteDeleteAsync(ct);
    }

    private static JournalEntryEntity ToEntity(JournalEntry e, DateTimeOffset parentTimestamp) => new()
    {
        PartitionKey = $"ledger/{parentTimestamp:yyyy-MM}",
        RowKey = e.Id.Value.ToString(),
        JournalEntryId = e.Id.Value,
        TransactionId = e.TransactionId.Value,
        AccountId = e.AccountId.Value,
        DebitAmount = e.DebitAmount,
        CreditAmount = e.CreditAmount,
        Currency = e.Currency
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
