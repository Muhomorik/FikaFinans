using FikaFinans.Application.Bank;
using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;
using FikaFinans.Domain.Bank.Ledger;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

public class LedgerService : ILedgerService
{
    private readonly ILogger _logger;
    private readonly BankDbContext _db;
    private readonly BankSimulator _clock;

    public LedgerService(ILogger logger, BankDbContext db, BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<TransactionId>> PostTransactionAsync(
        string description,
        IReadOnlyList<(AccountId AccountId, decimal Debit, decimal Credit, string Currency)> entries,
        TradingOrderId? relatedOrderId = null,
        CancellationToken ct = default)
    {
        var result = Transaction.Create(_clock.Now, description, entries, relatedOrderId);
        if (result.IsFailed)
        {
            _logger.Warn("Failed to post transaction: {0}", result.Errors[0].Message);
            return Result.Fail<TransactionId>(result.Errors);
        }

        var transaction = result.Value;
        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        _logger.Info("Posted transaction {0}: {1}", transaction.Id, description);
        return Result.Ok(transaction.Id);
    }

    public async Task<Money> GetAccountBalanceAsync(AccountId accountId, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account == null)
            return Money.Zero();

        var entries = await _db.JournalEntries
            .Where(e => e.AccountId == accountId)
            .ToListAsync(ct);

        var totalDebits = entries.Sum(e => e.DebitAmount);
        var totalCredits = entries.Sum(e => e.CreditAmount);

        var balance = account.Type switch
        {
            AccountType.Asset or AccountType.Expense => totalDebits - totalCredits,
            _ => totalCredits - totalDebits
        };

        return new Money(balance, account.Currency);
    }

    public async Task<IReadOnlyList<Transaction>> GetAllTransactionsAsync(CancellationToken ct = default)
    {
        return await _db.Transactions
            .Include(t => t.Entries)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Transaction>> GetTransactionsByOrderAsync(
        TradingOrderId orderId, CancellationToken ct = default)
    {
        return await _db.Transactions
            .Include(t => t.Entries)
            .Where(t => t.RelatedOrderId == orderId)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct);
    }
}
