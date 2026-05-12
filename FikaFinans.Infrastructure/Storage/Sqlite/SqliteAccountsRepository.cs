using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IAccountsRepository"/>. Maps the
/// existing domain <see cref="Account"/> EF entity ↔ <see cref="AccountEntity"/>
/// at the boundary; the domain <see cref="Account.CreateWithId"/> factory
/// handles inserts, EF tracked-entity property assignment handles updates.
/// </summary>
public sealed class SqliteAccountsRepository : IAccountsRepository
{
    private const string PartitionKeyValue = "accounts";

    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteAccountsRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<AccountEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!IsAccountsPartition(partitionKey)) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == rowKey, ct);
        return account is null ? null : ToEntity(account);
    }

    public async Task<IReadOnlyList<AccountEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        if (!IsAccountsPartition(partitionKey)) return Array.Empty<AccountEntity>();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Accounts.AsNoTracking().ToListAsync(ct);
        return rows.Select(ToEntity).ToList();
    }

    public async Task<AccountEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await GetAsync(PartitionKeyValue, code, ct);

    public async Task<AccountEntity?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var typedId = new AccountId(accountId);
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == typedId, ct);
        return account is null ? null : ToEntity(account);
    }

    public async Task UpsertAsync(AccountEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<AccountEntity> entities, CancellationToken ct = default)
    {
        TableBatchAsserts.EnsureSinglePartitionBatch(partitionKey, entities);
        await using var db = await _factory.CreateDbContextAsync(ct);
        foreach (var entity in entities)
            await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!IsAccountsPartition(partitionKey)) return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Accounts
            .Where(a => a.Code == rowKey)
            .ExecuteDeleteAsync(ct);
    }

    private static async Task UpsertCoreAsync(BankDbContext db, AccountEntity entity, CancellationToken ct)
    {
        var existing = await db.Accounts.FirstOrDefaultAsync(a => a.Code == entity.Code, ct);
        var type = Enum.Parse<AccountType>(entity.Type);

        if (existing is null)
        {
            var account = Account.CreateWithId(
                new AccountId(entity.AccountId),
                entity.Name,
                entity.Code,
                type,
                entity.Currency);
            db.Accounts.Add(account);
        }
        else
        {
            // Last-write-wins: replace mutable fields. Id (the EF key)
            // stays — Tables semantics route by Code (RowKey), and
            // changing the Guid would orphan downstream JournalEntry rows.
            db.Entry(existing).CurrentValues.SetValues(new
            {
                entity.Name,
                entity.Code,
                Type = type,
                entity.Currency
            });
        }
    }

    private static AccountEntity ToEntity(Account a) => new()
    {
        PartitionKey = PartitionKeyValue,
        RowKey = a.Code,
        AccountId = a.Id.Value,
        Name = a.Name,
        Code = a.Code,
        Type = a.Type.ToString(),
        Currency = a.Currency
    };

    private static bool IsAccountsPartition(string partitionKey)
        => string.Equals(partitionKey, PartitionKeyValue, StringComparison.Ordinal);
}
