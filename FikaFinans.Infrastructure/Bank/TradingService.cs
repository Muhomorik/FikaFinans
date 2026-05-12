using FikaFinans.Application.Bank;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Common;
using FluentResults;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Trading;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

/// <summary>
/// Trading service. Accounts, trading orders, and held positions all flow
/// through repos. <see cref="Domain.Bank.Funds.Fund"/> (NAV history, ISIN,
/// name) stays on direct EF until Phase 5 retires it.
/// </summary>
public class TradingService : ITradingService
{
    private const string PositionsPartition = "positions";

    private readonly ILogger _logger;
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly IAccountsRepository _accounts;
    private readonly ITradingOrdersRepository _tradingOrders;
    private readonly IPositionsRepository _positions;
    private readonly ILedgerService _ledgerService;
    private readonly BankSimulator _clock;

    public TradingService(
        ILogger logger,
        IDbContextFactory<BankDbContext> dbFactory,
        IAccountsRepository accounts,
        ITradingOrdersRepository tradingOrders,
        IPositionsRepository positions,
        ILedgerService ledgerService,
        BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _tradingOrders = tradingOrders ?? throw new ArgumentNullException(nameof(tradingOrders));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<TradingOrderId>> CreateBuyOrderAsync(
        FundId fundId, Money amount, CancellationToken ct = default)
    {
        var cashAccount = await _accounts.GetByCodeAsync("1000", ct);
        var pendingBuyAccount = await _accounts.GetByCodeAsync("1100", ct);

        if (cashAccount is null || pendingBuyAccount is null)
            return Result.Fail<TradingOrderId>("Chart of accounts not properly seeded.");

        var cashAccountId = new AccountId(cashAccount.AccountId);
        var pendingBuyAccountId = new AccountId(pendingBuyAccount.AccountId);

        var cashBalance = await _ledgerService.GetAccountBalanceAsync(cashAccountId, ct);
        if (cashBalance < amount)
            return Result.Fail<TradingOrderId>(
                $"Insufficient cash: available {cashBalance}, requested {amount}.");

        var order = TradingOrder.CreateBuyOrder(fundId, amount, _clock.Now);

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Reserve cash for buy order {order.Id}",
            new[] { (pendingBuyAccountId, amount.Amount, 0m, amount.Currency),
                    (cashAccountId, 0m, amount.Amount, amount.Currency) },
            order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail<TradingOrderId>(postResult.Errors);

        await _tradingOrders.UpsertAsync(await ToOrderEntityAsync(order, ct), ct);
        _logger.Info("Created buy order {0} for fund {1}, amount {2}", order.Id, fundId, amount);
        return Result.Ok(order.Id);
    }

    public async Task<Result<TradingOrderId>> CreateSellOrderAsync(
        FundId fundId, decimal units, CancellationToken ct = default)
    {
        var isin = await ResolveIsinAsync(fundId, ct);
        if (string.IsNullOrEmpty(isin))
            return Result.Fail<TradingOrderId>($"Fund {fundId} not found.");

        var position = await _positions.GetAsync(PositionsPartition, isin, ct);
        if (position is null || position.Units < units)
            return Result.Fail<TradingOrderId>(
                $"Insufficient units: available {position?.Units ?? 0:N4}, requested {units:N4}.");

        var allAccounts = await _accounts.QueryPartitionAsync("accounts", ct);
        var fundHoldingsAccount = allAccounts.FirstOrDefault(a =>
            a.Code.StartsWith("1200", StringComparison.Ordinal)
            && a.Name.Contains(fundId.Value.ToString(), StringComparison.Ordinal));
        var pendingSellAccount = await _accounts.GetByCodeAsync("2000", ct);

        if (fundHoldingsAccount is null || pendingSellAccount is null)
            return Result.Fail<TradingOrderId>("Chart of accounts not properly seeded.");

        // Cost basis of the sold units uses the position's preserved
        // average cost — same semantics as the retired FundHolding.
        var costBasisAmount = units * position.AvgCostPerUnit;
        var currency = "SEK";
        var order = TradingOrder.CreateSellOrder(fundId, units, _clock.Now, currency);

        var pendingSellAccountId = new AccountId(pendingSellAccount.AccountId);
        var fundHoldingsAccountId = new AccountId(fundHoldingsAccount.AccountId);

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Reserve units for sell order {order.Id}",
            new[] { (pendingSellAccountId, costBasisAmount, 0m, currency),
                    (fundHoldingsAccountId, 0m, costBasisAmount, currency) },
            order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail<TradingOrderId>(postResult.Errors);

        // Mutate the position: drop units, drop cost basis at avg-cost-per-unit.
        // CurrentValueKr restated against the average cost (settlement updates
        // it again with NAV). AvgCostPerUnit preserved across sells.
        var newUnits = position.Units - units;
        var newCostBasis = newUnits * position.AvgCostPerUnit;
        var newCurrentValue = position.CurrentValueKr * (position.Units > 0 ? newUnits / position.Units : 0m);
        await _positions.UpsertAsync(WithUpdates(position, newUnits, position.AvgCostPerUnit, newCurrentValue, newCostBasis, "sellReserve"), ct);

        await _tradingOrders.UpsertAsync(await ToOrderEntityAsync(order, ct), ct);
        _logger.Info("Created sell order {0} for fund {1}, {2:N4} units", order.Id, fundId, units);
        return Result.Ok(order.Id);
    }

    public async Task<Result> SettleOrderAsync(
        TradingOrderId orderId, decimal navPerUnit, CancellationToken ct = default)
    {
        var orderEntity = await FindOrderByIdAsync(orderId, ct);
        if (orderEntity is null)
            return Result.Fail($"Order {orderId} not found.");

        var order = ToDomainOrder(orderEntity);
        var settleResult = order.Settle(navPerUnit, _clock.Now);
        if (settleResult.IsFailed)
            return settleResult;

        return order.Side == OrderSide.Buy
            ? await SettleBuyOrderAsync(order, ct)
            : await SettleSellOrderAsync(order, ct);
    }

    private async Task<Result> SettleBuyOrderAsync(TradingOrder order, CancellationToken ct)
    {
        var pendingBuyAccount = await _accounts.GetByCodeAsync("1100", ct);
        if (pendingBuyAccount is null)
            return Result.Fail("Required accounts not found.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == order.FundId, ct);
        if (fund is null)
            return Result.Fail("Fund not found.");

        var fundHoldingsAccount = await GetOrCreateFundHoldingsAccountAsync(fund.Id, fund.Name, order.Currency, ct);

        var pendingBuyAccountId = new AccountId(pendingBuyAccount.AccountId);
        var fundHoldingsAccountId = new AccountId(fundHoldingsAccount.AccountId);

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Settle buy order {order.Id}: {order.SettledUnits:N4} units @ {order.SettlementNavPerUnit:N2}",
            new[] { (fundHoldingsAccountId, order.AmountValue, 0m, order.Currency),
                    (pendingBuyAccountId, 0m, order.AmountValue, order.Currency) },
            order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail(postResult.Errors);

        var settledUnits = order.SettledUnits!.Value;
        var existing = await _positions.GetAsync(PositionsPartition, fund.Isin.Value, ct);

        var oldUnits = existing?.Units ?? 0m;
        var oldCostBasis = existing?.CostBasisKr ?? 0m;
        var newUnits = oldUnits + settledUnits;
        var newCostBasis = oldCostBasis + order.AmountValue;
        var newAvgCost = newUnits > 0 ? newCostBasis / newUnits : 0m;
        var newCurrentValue = newUnits * order.SettlementNavPerUnit!.Value;

        await _positions.UpsertAsync(new PositionEntity
        {
            PartitionKey = PositionsPartition,
            RowKey = fund.Isin.Value,
            Isin = fund.Isin.Value,
            Name = existing?.Name ?? fund.Name,
            CurrentValueKr = newCurrentValue,
            CostBasisKr = newCostBasis,
            Units = newUnits,
            AvgCostPerUnit = newAvgCost,
            LastUpdatedAt = _clock.Now,
            Source = "buySettle"
        }, ct);

        await _tradingOrders.UpsertAsync(await ToOrderEntityAsync(order, ct), ct);
        _logger.Info("Settled buy order {0}: {1:N4} units of {2}", order.Id, order.SettledUnits, fund.Name);
        return Result.Ok();
    }

    private async Task<Result> SettleSellOrderAsync(TradingOrder order, CancellationToken ct)
    {
        var pendingSellAccount = await _accounts.GetByCodeAsync("2000", ct);
        var cashAccount = await _accounts.GetByCodeAsync("1000", ct);
        var gainsAccount = await _accounts.GetByCodeAsync("4000", ct);
        var lossesAccount = await _accounts.GetByCodeAsync("5000", ct);

        if (pendingSellAccount is null || cashAccount is null || gainsAccount is null || lossesAccount is null)
            return Result.Fail("Required accounts not found.");

        var proceeds = order.SettledUnits!.Value * order.SettlementNavPerUnit!.Value;

        var reservationTxns = await _ledgerService.GetTransactionsByOrderAsync(order.Id, ct);
        var reservationEntry = reservationTxns
            .SelectMany(t => t.Entries)
            .FirstOrDefault(e => e.AccountId.Value == pendingSellAccount.AccountId && e.DebitAmount > 0);

        var costBasis = reservationEntry?.DebitAmount ?? 0;
        var gainLoss = proceeds - costBasis;

        var entries = new List<(AccountId AccountId, decimal Debit, decimal Credit, string Currency)>
        {
            (new AccountId(cashAccount.AccountId), proceeds, 0m, order.Currency),
            (new AccountId(pendingSellAccount.AccountId), 0m, costBasis, order.Currency)
        };

        if (gainLoss > 0)
            entries.Add((new AccountId(gainsAccount.AccountId), 0m, gainLoss, order.Currency));
        else if (gainLoss < 0)
            entries.Add((new AccountId(lossesAccount.AccountId), Math.Abs(gainLoss), 0m, order.Currency));

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Settle sell order {order.Id}: {order.SettledUnits:N4} units @ {order.SettlementNavPerUnit:N2}, {(gainLoss >= 0 ? "gain" : "loss")} {Math.Abs(gainLoss):N2}",
            entries, order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail(postResult.Errors);

        // Position units were already decremented at sell-create. On settle,
        // restate CurrentValueKr against the realized NAV so the WPF position
        // value reflects post-settlement reality.
        var isin = await ResolveIsinAsync(order.FundId, ct);
        var existing = !string.IsNullOrEmpty(isin)
            ? await _positions.GetAsync(PositionsPartition, isin, ct)
            : null;
        if (existing is not null)
        {
            var newCurrentValue = existing.Units * order.SettlementNavPerUnit!.Value;
            await _positions.UpsertAsync(WithUpdates(
                existing, existing.Units, existing.AvgCostPerUnit,
                newCurrentValue, existing.CostBasisKr, "sellSettle"), ct);
        }

        await _tradingOrders.UpsertAsync(await ToOrderEntityAsync(order, ct), ct);
        _logger.Info("Settled sell order {0}: proceeds {1:N2}, {2} {3:N2}",
            order.Id, proceeds, gainLoss >= 0 ? "gain" : "loss", Math.Abs(gainLoss));
        return Result.Ok();
    }

    private async Task<AccountEntity> GetOrCreateFundHoldingsAccountAsync(
        FundId fundId, string fundName, string currency, CancellationToken ct)
    {
        var allAccounts = await _accounts.QueryPartitionAsync("accounts", ct);
        var existing = allAccounts.FirstOrDefault(a =>
            a.Code.StartsWith("1200", StringComparison.Ordinal)
            && a.Name.Contains(fundId.Value.ToString(), StringComparison.Ordinal));
        if (existing is not null)
            return existing;

        var twelveCount = allAccounts.Count(a => a.Code.StartsWith("12", StringComparison.Ordinal));
        var nextCode = twelveCount > 0 ? $"12{twelveCount:D2}" : "1200";

        var account = Domain.Bank.Accounts.Account.Create(
            $"Fund Holdings - {fundName} ({fundId})",
            nextCode,
            Domain.Bank.Accounts.AccountType.Asset,
            currency);

        var entity = new AccountEntity
        {
            PartitionKey = "accounts",
            RowKey = account.Code,
            AccountId = account.Id.Value,
            Name = account.Name,
            Code = account.Code,
            Type = account.Type.ToString(),
            Currency = account.Currency
        };
        await _accounts.UpsertAsync(entity, ct);
        return entity;
    }

    public async Task<IReadOnlyList<TradingOrder>> GetPendingOrdersAsync(CancellationToken ct = default)
    {
        var entities = await _tradingOrders.QueryByStatusAsync(OrderStatus.Pending.ToString(), ct);
        return entities
            .OrderBy(e => e.CreatedAt)
            .Select(ToDomainOrder)
            .ToList();
    }

    public async Task<IReadOnlyList<TradingOrder>> GetAllOrdersAsync(CancellationToken ct = default)
    {
        var entities = await _tradingOrders.QueryAllAsync(ct);
        return entities
            .OrderByDescending(e => e.CreatedAt)
            .Select(ToDomainOrder)
            .ToList();
    }

    private async Task<TradingOrderEntity?> FindOrderByIdAsync(TradingOrderId orderId, CancellationToken ct)
    {
        // No PK/RK lookup-by-id (RowKey is "{isin}/{side}", PK is the date) —
        // a cross-partition scan is the honest answer until orders gain a
        // by-id typed lookup. The hot set ("Pending") is small in practice.
        var all = await _tradingOrders.QueryAllAsync(ct);
        return all.FirstOrDefault(o => o.OrderId == orderId.Value);
    }

    private async Task<TradingOrderEntity> ToOrderEntityAsync(TradingOrder order, CancellationToken ct)
    {
        var isin = await ResolveIsinAsync(order.FundId, ct);
        return new TradingOrderEntity
        {
            PartitionKey = $"orders/{order.CreatedAt:yyyy-MM-dd}",
            RowKey = $"{isin}/{order.Side}",
            OrderId = order.Id.Value,
            FundId = order.FundId.Value,
            Isin = isin,
            Side = order.Side.ToString(),
            AmountValue = order.AmountValue,
            Currency = order.Currency,
            Units = order.Units,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            SettledAt = order.SettledAt,
            SettlementNavPerUnit = order.SettlementNavPerUnit,
            SettledUnits = order.SettledUnits
        };
    }

    private async Task<string> ResolveIsinAsync(FundId fundId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fund = await db.Funds.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fundId, ct);
        return fund?.Isin.Value ?? string.Empty;
    }

    private static PositionEntity WithUpdates(
        PositionEntity src,
        decimal units, decimal avgCost,
        decimal currentValue, decimal costBasis,
        string source)
        => new()
        {
            PartitionKey = src.PartitionKey,
            RowKey = src.RowKey,
            Isin = src.Isin,
            Name = src.Name,
            CurrentValueKr = currentValue,
            CostBasisKr = costBasis,
            Units = units,
            AvgCostPerUnit = avgCost,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Source = source
        };

    private static TradingOrder ToDomainOrder(TradingOrderEntity e) =>
        TradingOrder.Rehydrate(
            new TradingOrderId(e.OrderId),
            new FundId(e.FundId),
            Enum.Parse<OrderSide>(e.Side),
            e.AmountValue,
            e.Currency,
            e.Units,
            Enum.Parse<OrderStatus>(e.Status),
            e.CreatedAt,
            e.SettledAt,
            e.SettlementNavPerUnit,
            e.SettledUnits);
}
