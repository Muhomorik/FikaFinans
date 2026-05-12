using FikaFinans.Application.Bank;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;
using FikaFinans.Domain.Bank.Ledger;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

/// <summary>
/// Ledger service, repo-backed. Reads stitch <see cref="Transaction"/>
/// headers and <see cref="JournalEntry"/> rows in memory — replacing the
/// EF nav prop pattern with a Tables-friendly two-reads + group-by-id join.
/// Writes go through repo upserts; the ledger no longer touches EF directly.
/// </summary>
public class LedgerService : ILedgerService
{
    private readonly ILogger _logger;
    private readonly IAccountsRepository _accounts;
    private readonly ITransactionsRepository _transactions;
    private readonly IJournalEntriesRepository _journalEntries;
    private readonly BankSimulator _clock;

    public LedgerService(
        ILogger logger,
        IAccountsRepository accounts,
        ITransactionsRepository transactions,
        IJournalEntriesRepository journalEntries,
        BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _journalEntries = journalEntries ?? throw new ArgumentNullException(nameof(journalEntries));
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
        var partitionKey = LedgerPartition(transaction.Timestamp);

        var txnEntity = new TransactionEntity
        {
            PartitionKey = partitionKey,
            RowKey = transaction.Id.Value.ToString(),
            TransactionId = transaction.Id.Value,
            Timestamp = transaction.Timestamp,
            Description = transaction.Description,
            Status = transaction.Status.ToString(),
            RelatedOrderId = transaction.RelatedOrderId?.Value
        };

        var entryEntities = transaction.Entries.Select(e => new JournalEntryEntity
        {
            PartitionKey = partitionKey,
            RowKey = e.Id.Value.ToString(),
            JournalEntryId = e.Id.Value,
            TransactionId = e.TransactionId.Value,
            AccountId = e.AccountId.Value,
            DebitAmount = e.DebitAmount,
            CreditAmount = e.CreditAmount,
            Currency = e.Currency
        }).ToList();

        await _transactions.UpsertAsync(txnEntity, ct);
        if (entryEntities.Count > 0)
            await _journalEntries.UpsertBatchAsync(partitionKey, entryEntities, ct);

        _logger.Info("Posted transaction {0}: {1}", transaction.Id, description);
        return Result.Ok(transaction.Id);
    }

    public async Task<Money> GetAccountBalanceAsync(AccountId accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId.Value, ct);
        if (account is null)
            return Money.Zero();

        var entries = await _journalEntries.QueryByAccountAsync(accountId.Value, ct);
        var totalDebits = entries.Sum(e => e.DebitAmount);
        var totalCredits = entries.Sum(e => e.CreditAmount);

        var type = Enum.Parse<AccountType>(account.Type);
        var balance = type switch
        {
            AccountType.Asset or AccountType.Expense => totalDebits - totalCredits,
            _ => totalCredits - totalDebits
        };

        return new Money(balance, account.Currency);
    }

    public async Task<IReadOnlyList<Transaction>> GetAllTransactionsAsync(CancellationToken ct = default)
    {
        var txnEntities = await _transactions.QueryAllAsync(ct);
        if (txnEntities.Count == 0) return Array.Empty<Transaction>();

        var entryEntities = await _journalEntries.QueryByTransactionIdsAsync(
            txnEntities.Select(t => t.TransactionId).ToList(), ct);

        return Stitch(txnEntities, entryEntities)
            .OrderByDescending(t => t.Timestamp)
            .ToList();
    }

    public async Task<IReadOnlyList<Transaction>> GetTransactionsByOrderAsync(
        TradingOrderId orderId, CancellationToken ct = default)
    {
        var txnEntities = await _transactions.GetByRelatedOrderAsync(orderId.Value, ct);
        if (txnEntities.Count == 0) return Array.Empty<Transaction>();

        var entryEntities = await _journalEntries.QueryByTransactionIdsAsync(
            txnEntities.Select(t => t.TransactionId).ToList(), ct);

        return Stitch(txnEntities, entryEntities)
            .OrderByDescending(t => t.Timestamp)
            .ToList();
    }

    private static IEnumerable<Transaction> Stitch(
        IReadOnlyList<TransactionEntity> txnEntities,
        IReadOnlyList<JournalEntryEntity> entryEntities)
    {
        var entriesByTxn = entryEntities
            .GroupBy(e => e.TransactionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<JournalEntryEntity>)g.ToList());

        foreach (var t in txnEntities)
        {
            var entries = entriesByTxn.TryGetValue(t.TransactionId, out var list)
                ? list.Select(ToDomainEntry).ToList()
                : new List<JournalEntry>();

            yield return Transaction.Rehydrate(
                new TransactionId(t.TransactionId),
                t.Timestamp,
                t.Description,
                Enum.Parse<TransactionStatus>(t.Status),
                t.RelatedOrderId.HasValue ? new TradingOrderId(t.RelatedOrderId.Value) : null,
                entries);
        }
    }

    private static JournalEntry ToDomainEntry(JournalEntryEntity e) =>
        JournalEntry.Rehydrate(
            new JournalEntryId(e.JournalEntryId),
            new TransactionId(e.TransactionId),
            new AccountId(e.AccountId),
            e.DebitAmount,
            e.CreditAmount,
            e.Currency);

    private static string LedgerPartition(DateTimeOffset timestamp) =>
        $"ledger/{timestamp:yyyy-MM}";
}
