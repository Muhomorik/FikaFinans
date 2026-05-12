using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Trading;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="ITradingOrdersRepository"/>. Fully functional —
/// chunk 4 lit up upsert and added the cross-partition status/all queries
/// the bank-sim's WPF tab and settlement engine need.
/// </summary>
/// <remarks>
/// PartitionKey shape <c>"orders/{yyyy-MM-dd}"</c> (from <c>CreatedAt</c>)
/// and RowKey shape <c>"{isin}/{side}"</c> are reconstructed on read.
/// The domain <see cref="TradingOrder"/> doesn't store ISIN, so reads
/// look it up from the related <see cref="Fund"/>; the fund table is
/// pulled into memory because <c>Isin.Value</c> doesn't translate to SQL
/// (matches the in-memory-filter pattern from <c>BankCsvImporter</c>).
/// </remarks>
public sealed class SqliteTradingOrdersRepository : ITradingOrdersRepository
{
    private readonly IDbContextFactory<BankDbContext> _factory;

    public SqliteTradingOrdersRepository(IDbContextFactory<BankDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<TradingOrderEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        var (date, ok) = TryParseOrdersPartition(partitionKey);
        if (!ok) return null;
        var (isin, side, rkOk) = TryParseOrderRowKey(rowKey);
        if (!rkOk) return null;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var fund = await FindFundByIsinAsync(db, isin, ct);
        if (fund is null) return null;

        var order = await db.TradingOrders.AsNoTracking()
            .Where(o => o.FundId == fund.Id
                     && o.Side == side
                     && o.CreatedAt >= date
                     && o.CreatedAt < date.AddDays(1))
            .FirstOrDefaultAsync(ct);

        return order is null ? null : ToEntity(order, fund.Isin.Value);
    }

    public async Task<IReadOnlyList<TradingOrderEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        var (date, ok) = TryParseOrdersPartition(partitionKey);
        if (!ok) return Array.Empty<TradingOrderEntity>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var orders = await db.TradingOrders.AsNoTracking()
            .Where(o => o.CreatedAt >= date && o.CreatedAt < date.AddDays(1))
            .ToListAsync(ct);
        if (orders.Count == 0) return Array.Empty<TradingOrderEntity>();

        var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
        var isinByFundId = allFunds.ToDictionary(f => f.Id, f => f.Isin.Value);

        return orders.Select(o => ToEntity(o, isinByFundId.GetValueOrDefault(o.FundId, string.Empty))).ToList();
    }

    public async Task UpsertAsync(TradingOrderEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = await _factory.CreateDbContextAsync(ct);
        await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(string partitionKey, IReadOnlyList<TradingOrderEntity> entities, CancellationToken ct = default)
    {
        TableBatchAsserts.EnsureSinglePartitionBatch(partitionKey, entities);
        await using var db = await _factory.CreateDbContextAsync(ct);
        foreach (var entity in entities)
            await UpsertCoreAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TradingOrderEntity>> QueryByStatusAsync(string status, CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrderStatus>(status, out var typedStatus))
            return Array.Empty<TradingOrderEntity>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var orders = await db.TradingOrders.AsNoTracking()
            .Where(o => o.Status == typedStatus)
            .ToListAsync(ct);
        return await ProjectWithIsinAsync(db, orders, ct);
    }

    public async Task<IReadOnlyList<TradingOrderEntity>> QueryAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var orders = await db.TradingOrders.AsNoTracking().ToListAsync(ct);
        return await ProjectWithIsinAsync(db, orders, ct);
    }

    private static async Task<IReadOnlyList<TradingOrderEntity>> ProjectWithIsinAsync(
        BankDbContext db, IReadOnlyList<TradingOrder> orders, CancellationToken ct)
    {
        if (orders.Count == 0) return Array.Empty<TradingOrderEntity>();
        var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
        var isinByFundId = allFunds.ToDictionary(f => f.Id, f => f.Isin.Value);
        return orders
            .Select(o => ToEntity(o, isinByFundId.GetValueOrDefault(o.FundId, string.Empty)))
            .ToList();
    }

    private static async Task UpsertCoreAsync(BankDbContext db, TradingOrderEntity entity, CancellationToken ct)
    {
        var orderId = new TradingOrderId(entity.OrderId);
        var existing = await db.TradingOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        var status = Enum.Parse<OrderStatus>(entity.Status);
        var side = Enum.Parse<OrderSide>(entity.Side);

        if (existing is null)
        {
            var order = TradingOrder.Rehydrate(
                orderId,
                new FundId(entity.FundId),
                side,
                entity.AmountValue,
                entity.Currency,
                entity.Units,
                status,
                entity.CreatedAt,
                entity.SettledAt,
                entity.SettlementNavPerUnit,
                entity.SettledUnits);
            db.TradingOrders.Add(order);
        }
        else
        {
            // Last-write-wins on mutable settlement fields plus Status.
            // Side/CreatedAt/AmountValue/FundId are conceptually immutable
            // for an order's lifetime, but we mirror the row exactly so a
            // late corrective write isn't silently dropped.
            db.Entry(existing).CurrentValues.SetValues(new
            {
                FundId = new FundId(entity.FundId),
                Side = side,
                entity.AmountValue,
                entity.Currency,
                entity.Units,
                Status = status,
                entity.CreatedAt,
                entity.SettledAt,
                entity.SettlementNavPerUnit,
                entity.SettledUnits
            });
        }
    }

    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        var (date, ok) = TryParseOrdersPartition(partitionKey);
        if (!ok) return;
        var (isin, side, rkOk) = TryParseOrderRowKey(rowKey);
        if (!rkOk) return;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var fund = await FindFundByIsinAsync(db, isin, ct);
        if (fund is null) return;

        await db.TradingOrders
            .Where(o => o.FundId == fund.Id
                     && o.Side == side
                     && o.CreatedAt >= date
                     && o.CreatedAt < date.AddDays(1))
            .ExecuteDeleteAsync(ct);
    }

    private static async Task<Fund?> FindFundByIsinAsync(BankDbContext db, string isin, CancellationToken ct)
    {
        var allFunds = await db.Funds.AsNoTracking().ToListAsync(ct);
        return allFunds.FirstOrDefault(f =>
            string.Equals(f.Isin.Value, isin, StringComparison.OrdinalIgnoreCase));
    }

    private static TradingOrderEntity ToEntity(TradingOrder o, string isin) => new()
    {
        PartitionKey = $"orders/{o.CreatedAt:yyyy-MM-dd}",
        RowKey = $"{isin}/{o.Side}",
        OrderId = o.Id.Value,
        FundId = o.FundId.Value,
        Isin = isin,
        Side = o.Side.ToString(),
        AmountValue = o.AmountValue,
        Currency = o.Currency,
        Units = o.Units,
        Status = o.Status.ToString(),
        CreatedAt = o.CreatedAt,
        SettledAt = o.SettledAt,
        SettlementNavPerUnit = o.SettlementNavPerUnit,
        SettledUnits = o.SettledUnits
    };

    private static (DateTime Date, bool Ok) TryParseOrdersPartition(string partitionKey)
    {
        const string prefix = "orders/";
        if (!partitionKey.StartsWith(prefix, StringComparison.Ordinal))
            return (default, false);
        var datePart = partitionKey[prefix.Length..];
        return DateTime.TryParseExact(datePart, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? (DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc), true)
            : (default, false);
    }

    private static (string Isin, OrderSide Side, bool Ok) TryParseOrderRowKey(string rowKey)
    {
        var slash = rowKey.IndexOf('/');
        if (slash <= 0 || slash == rowKey.Length - 1)
            return (string.Empty, default, false);
        var isin = rowKey[..slash];
        var sidePart = rowKey[(slash + 1)..];
        return Enum.TryParse<OrderSide>(sidePart, out var side)
            ? (isin, side, true)
            : (string.Empty, default, false);
    }
}
