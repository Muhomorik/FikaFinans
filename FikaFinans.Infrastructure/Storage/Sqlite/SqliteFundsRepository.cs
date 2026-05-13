using System.Globalization;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IFundsRepository"/>. Maps domain
/// <see cref="Fund"/> / <see cref="NavSnapshot"/> ↔
/// <see cref="FundEntity"/> / <see cref="NavSnapshotEntity"/> at the
/// boundary. Inserts go through the non-validating
/// <see cref="Fund.Rehydrate"/> / <see cref="NavSnapshot.Rehydrate"/>
/// factories; updates use tracked-entity replacement. Each call opens a
/// fresh short-lived <see cref="BankDbContext"/> via the factory.
/// </summary>
public sealed class SqliteFundsRepository : IFundsRepository
{
    private const string FundsPartition = "funds";
    private const string NavPartitionPrefix = "nav/";
    private const string IsoRoundTripFormat = "o";

    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteFundsRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<FundEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!IsFundsPartition(partitionKey)) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var fund = await LoadFundByIsinAsync(db, rowKey, ct);
        return fund is null ? null : ToEntity(fund);
    }

    public async Task<IReadOnlyList<FundEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        if (!IsFundsPartition(partitionKey)) return Array.Empty<FundEntity>();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var funds = await db.Funds.AsNoTracking().ToListAsync(ct);
        return funds.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(FundEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertFundCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<FundEntity> entities, CancellationToken ct = default)
    {
        TableBatchAsserts.EnsureSinglePartitionBatch(partitionKey, entities);
        await using var db = await _factory.CreateDbContextAsync(ct);
        foreach (var entity in entities)
            await UpsertFundCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (IsFundsPartition(partitionKey))
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var allFunds = await db.Funds.ToListAsync(ct);
            var fund = allFunds.FirstOrDefault(f => f.Isin.Value == rowKey);
            if (fund is null) return;
            // NAV history must be dropped explicitly now that the cascade FK is gone.
            await db.NavSnapshots.Where(n => n.FundId == fund.Id).ExecuteDeleteAsync(ct);
            db.Funds.Remove(fund);
            await db.SaveChangesAsync(ct);
            return;
        }

        if (partitionKey.StartsWith(NavPartitionPrefix, StringComparison.Ordinal))
        {
            var isin = partitionKey[NavPartitionPrefix.Length..];
            if (!DateTimeOffset.TryParse(rowKey, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date))
                return;
            await using var db = await _factory.CreateDbContextAsync(ct);
            var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
            var fund = allFunds.FirstOrDefault(f => f.Isin.Value == isin);
            if (fund is null) return;
            await db.NavSnapshots
                .Where(n => n.FundId == fund.Id && n.Date == date)
                .ExecuteDeleteAsync(ct);
        }
    }

    public async Task<FundEntity?> GetByIsinAsync(Isin isin, CancellationToken ct = default)
        => await GetAsync(FundsPartition, isin.Value, ct);

    public async Task<FundEntity?> GetByIdAsync(FundId fundId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var fund = await db.Funds.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fundId, ct);
        return fund is null ? null : ToEntity(fund);
    }

    public async Task<decimal?> GetLatestNavAsync(Isin isin, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
        var fund = allFunds.FirstOrDefault(f => f.Isin.Value == isin.Value);
        if (fund is null) return null;
        return await LoadLatestNavAsync(db, fund.Id, ct);
    }

    public async Task<decimal?> GetLatestNavByFundIdAsync(FundId fundId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadLatestNavAsync(db, fundId, ct);
    }

    public async Task<IReadOnlyList<NavSnapshotEntity>> QueryNavHistoryAsync(Isin isin, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
        var fund = allFunds.FirstOrDefault(f => f.Isin.Value == isin.Value);
        if (fund is null) return Array.Empty<NavSnapshotEntity>();

        var rows = await db.NavSnapshots.AsNoTracking()
            .Where(n => n.FundId == fund.Id)
            .OrderBy(n => n.Date)
            .ToListAsync(ct);
        return rows.Select(n => ToNavEntity(n, isin.Value)).ToList();
    }

    public async Task UpsertNavAsync(NavSnapshotEntity nav, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nav);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var fundId = new FundId(nav.FundId);

        var existing = await db.NavSnapshots
            .FirstOrDefaultAsync(n => n.FundId == fundId && n.Date == nav.Date, ct);

        if (existing is null)
        {
            var snapshot = NavSnapshot.Rehydrate(
                new NavSnapshotId(nav.NavSnapshotId == Guid.Empty ? Guid.NewGuid() : nav.NavSnapshotId),
                fundId, nav.Date, nav.NavPerUnit);
            db.NavSnapshots.Add(snapshot);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(new
            {
                NavPerUnit = nav.NavPerUnit
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Fund?> LoadFundByIsinAsync(BankDbContext db, string isin, CancellationToken ct)
    {
        // Isin.Value doesn't translate to SQL — same in-memory filter
        // pattern as BankCsvImporter / SqliteTradingOrdersRepository.
        var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
        return allFunds.FirstOrDefault(f => f.Isin.Value == isin);
    }

    private static async Task<decimal?> LoadLatestNavAsync(BankDbContext db, FundId fundId, CancellationToken ct)
    {
        var latest = await db.NavSnapshots.AsNoTracking()
            .Where(n => n.FundId == fundId)
            .OrderByDescending(n => n.Date)
            .Select(n => (decimal?)n.NavPerUnit)
            .FirstOrDefaultAsync(ct);
        return latest;
    }

    private static async Task UpsertFundCoreAsync(BankDbContext db, FundEntity entity, CancellationToken ct)
    {
        var allFunds = await db.Funds.ToListAsync(ct);
        var existing = allFunds.FirstOrDefault(f => f.Isin.Value == entity.Isin);

        if (existing is null)
        {
            var fund = Fund.Rehydrate(
                new FundId(entity.FundId == Guid.Empty ? Guid.NewGuid() : entity.FundId),
                entity.Name,
                new Isin(entity.Isin),
                entity.Currency,
                Array.Empty<NavSnapshot>());
            db.Funds.Add(fund);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(new
            {
                Name = entity.Name,
                Currency = entity.Currency
            });
        }
    }

    private static FundEntity ToEntity(Fund f) => new()
    {
        PartitionKey = FundsPartition,
        RowKey = f.Isin.Value,
        FundId = f.Id.Value,
        Name = f.Name,
        Isin = f.Isin.Value,
        Currency = f.Currency
    };

    private static NavSnapshotEntity ToNavEntity(NavSnapshot n, string isin) => new()
    {
        PartitionKey = NavPartitionPrefix + isin,
        RowKey = n.Date.ToString(IsoRoundTripFormat, CultureInfo.InvariantCulture),
        NavSnapshotId = n.Id.Value,
        FundId = n.FundId.Value,
        Isin = isin,
        Date = n.Date,
        NavPerUnit = n.NavPerUnit
    };

    private static bool IsFundsPartition(string partitionKey)
        => string.Equals(partitionKey, FundsPartition, StringComparison.Ordinal);
}
