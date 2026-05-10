using FikaFinans.Application.Bank;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Holdings;
using FluentResults;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Trading;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

public class TradingService : ITradingService
{
    private readonly ILogger _logger;
    private readonly BankDbContext _db;
    private readonly ILedgerService _ledgerService;
    private readonly BankSimulator _clock;

    public TradingService(ILogger logger, BankDbContext db, ILedgerService ledgerService, BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<TradingOrderId>> CreateBuyOrderAsync(
        FundId fundId, Money amount, CancellationToken ct = default)
    {
        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000", ct);
        var pendingBuyAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1100", ct);

        if (cashAccount == null || pendingBuyAccount == null)
            return Result.Fail<TradingOrderId>("Chart of accounts not properly seeded.");

        var cashBalance = await _ledgerService.GetAccountBalanceAsync(cashAccount.Id, ct);
        if (cashBalance < amount)
            return Result.Fail<TradingOrderId>(
                $"Insufficient cash: available {cashBalance}, requested {amount}.");

        var order = TradingOrder.CreateBuyOrder(fundId, amount, _clock.Now);
        _db.TradingOrders.Add(order);

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Reserve cash for buy order {order.Id}",
            new[] { (pendingBuyAccount.Id, amount.Amount, 0m, amount.Currency),
                    (cashAccount.Id, 0m, amount.Amount, amount.Currency) },
            order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail<TradingOrderId>(postResult.Errors);

        await _db.SaveChangesAsync(ct);
        _logger.Info("Created buy order {0} for fund {1}, amount {2}", order.Id, fundId, amount);
        return Result.Ok(order.Id);
    }

    public async Task<Result<TradingOrderId>> CreateSellOrderAsync(
        FundId fundId, decimal units, CancellationToken ct = default)
    {
        var holding = await _db.FundHoldings.FirstOrDefaultAsync(h => h.FundId == fundId, ct);
        if (holding == null || holding.Units < units)
            return Result.Fail<TradingOrderId>(
                $"Insufficient units: available {holding?.Units ?? 0:N4}, requested {units:N4}.");

        var fundHoldingsAccount = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Code.StartsWith("1200") && a.Name.Contains(fundId.Value.ToString()), ct);
        var pendingSellAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000", ct);

        if (fundHoldingsAccount == null || pendingSellAccount == null)
            return Result.Fail<TradingOrderId>("Chart of accounts not properly seeded.");

        var costBasisResult = holding.RemoveUnits(units);
        if (costBasisResult.IsFailed)
            return Result.Fail<TradingOrderId>(costBasisResult.Errors);

        var costBasis = costBasisResult.Value;
        var order = TradingOrder.CreateSellOrder(fundId, units, _clock.Now, costBasis.Currency);
        _db.TradingOrders.Add(order);

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Reserve units for sell order {order.Id}",
            new[] { (pendingSellAccount.Id, costBasis.Amount, 0m, costBasis.Currency),
                    (fundHoldingsAccount.Id, 0m, costBasis.Amount, costBasis.Currency) },
            order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail<TradingOrderId>(postResult.Errors);

        await _db.SaveChangesAsync(ct);
        _logger.Info("Created sell order {0} for fund {1}, {2:N4} units", order.Id, fundId, units);
        return Result.Ok(order.Id);
    }

    public async Task<Result> SettleOrderAsync(
        TradingOrderId orderId, decimal navPerUnit, CancellationToken ct = default)
    {
        var order = await _db.TradingOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null)
            return Result.Fail($"Order {orderId} not found.");

        var settleResult = order.Settle(navPerUnit, _clock.Now);
        if (settleResult.IsFailed)
            return settleResult;

        return order.Side == OrderSide.Buy
            ? await SettleBuyOrderAsync(order, ct)
            : await SettleSellOrderAsync(order, ct);
    }

    private async Task<Result> SettleBuyOrderAsync(TradingOrder order, CancellationToken ct)
    {
        var pendingBuyAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1100", ct);
        var fund = await _db.Funds.FirstOrDefaultAsync(f => f.Id == order.FundId, ct);

        if (pendingBuyAccount == null || fund == null)
            return Result.Fail("Required accounts/funds not found.");

        var fundHoldingsAccount = await GetOrCreateFundHoldingsAccountAsync(fund.Id, fund.Name, order.Currency, ct);

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Settle buy order {order.Id}: {order.SettledUnits:N4} units @ {order.SettlementNavPerUnit:N2}",
            new[] { (fundHoldingsAccount.Id, order.AmountValue, 0m, order.Currency),
                    (pendingBuyAccount.Id, 0m, order.AmountValue, order.Currency) },
            order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail(postResult.Errors);

        var holding = await _db.FundHoldings.FirstOrDefaultAsync(h => h.FundId == order.FundId, ct);
        if (holding == null)
        {
            holding = FundHolding.Create(order.FundId, order.Currency);
            _db.FundHoldings.Add(holding);
        }

        holding.AddUnits(order.SettledUnits!.Value, new Money(order.AmountValue, order.Currency));
        await _db.SaveChangesAsync(ct);

        _logger.Info("Settled buy order {0}: {1:N4} units of {2}", order.Id, order.SettledUnits, fund.Name);
        return Result.Ok();
    }

    private async Task<Result> SettleSellOrderAsync(TradingOrder order, CancellationToken ct)
    {
        var pendingSellAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000", ct);
        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000", ct);
        var gainsAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "4000", ct);
        var lossesAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "5000", ct);

        if (pendingSellAccount == null || cashAccount == null || gainsAccount == null || lossesAccount == null)
            return Result.Fail("Required accounts not found.");

        var proceeds = order.SettledUnits!.Value * order.SettlementNavPerUnit!.Value;

        var reservationTxns = await _ledgerService.GetTransactionsByOrderAsync(order.Id, ct);
        var reservationEntry = reservationTxns
            .SelectMany(t => t.Entries)
            .FirstOrDefault(e => e.AccountId == pendingSellAccount.Id && e.DebitAmount > 0);

        var costBasis = reservationEntry?.DebitAmount ?? 0;
        var gainLoss = proceeds - costBasis;

        var entries = new List<(Domain.Bank.Identifiers.AccountId AccountId, decimal Debit, decimal Credit, string Currency)>
        {
            (cashAccount.Id, proceeds, 0m, order.Currency),
            (pendingSellAccount.Id, 0m, costBasis, order.Currency)
        };

        if (gainLoss > 0)
            entries.Add((gainsAccount.Id, 0m, gainLoss, order.Currency));
        else if (gainLoss < 0)
            entries.Add((lossesAccount.Id, Math.Abs(gainLoss), 0m, order.Currency));

        var postResult = await _ledgerService.PostTransactionAsync(
            $"Settle sell order {order.Id}: {order.SettledUnits:N4} units @ {order.SettlementNavPerUnit:N2}, {(gainLoss >= 0 ? "gain" : "loss")} {Math.Abs(gainLoss):N2}",
            entries, order.Id, ct);

        if (postResult.IsFailed)
            return Result.Fail(postResult.Errors);

        await _db.SaveChangesAsync(ct);
        _logger.Info("Settled sell order {0}: proceeds {1:N2}, {2} {3:N2}",
            order.Id, proceeds, gainLoss >= 0 ? "gain" : "loss", Math.Abs(gainLoss));
        return Result.Ok();
    }

    private async Task<Domain.Bank.Accounts.Account> GetOrCreateFundHoldingsAccountAsync(
        FundId fundId, string fundName, string currency, CancellationToken ct)
    {
        var existing = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Code.StartsWith("1200") && a.Name.Contains(fundId.Value.ToString()), ct);
        if (existing != null)
            return existing;

        var count = await _db.Accounts.CountAsync(a => a.Code.StartsWith("12"), ct);
        var nextCode = count > 0 ? $"12{count:D2}" : "1200";

        var account = Domain.Bank.Accounts.Account.Create(
            $"Fund Holdings - {fundName} ({fundId})",
            nextCode,
            Domain.Bank.Accounts.AccountType.Asset,
            currency);

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);
        return account;
    }

    public async Task<IReadOnlyList<TradingOrder>> GetPendingOrdersAsync(CancellationToken ct = default)
    {
        return await _db.TradingOrders
            .Where(o => o.Status == OrderStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TradingOrder>> GetAllOrdersAsync(CancellationToken ct = default)
    {
        return await _db.TradingOrders
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);
    }
}
