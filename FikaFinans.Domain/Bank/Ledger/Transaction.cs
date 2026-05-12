using System.Diagnostics;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;

namespace FikaFinans.Domain.Bank.Ledger;

[DebuggerDisplay("Txn {Id}: {Description} ({Status})")]
public class Transaction
{
    public TransactionId Id { get; private init; }
    public DateTimeOffset Timestamp { get; private init; }
    public string Description { get; private init; } = string.Empty;
    public TransactionStatus Status { get; private set; }
    public TradingOrderId? RelatedOrderId { get; private init; }

    private readonly List<JournalEntry> _entries = new();
    public IReadOnlyList<JournalEntry> Entries => _entries.AsReadOnly();

    private Transaction() { }

    public static Result<Transaction> Create(
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(AccountId AccountId, decimal Debit, decimal Credit, string Currency)> entries,
        TradingOrderId? relatedOrderId = null)
    {
        if (entries.Count < 2)
            return Result.Fail<Transaction>("A transaction must have at least 2 journal entries.");

        var totalDebits = entries.Sum(e => e.Debit);
        var totalCredits = entries.Sum(e => e.Credit);

        if (Math.Abs(totalDebits - totalCredits) > 0.0001m)
            return Result.Fail<Transaction>(
                $"Transaction is unbalanced: total debits ({totalDebits:N2}) != total credits ({totalCredits:N2}).");

        foreach (var entry in entries)
        {
            if (entry.Debit > 0 && entry.Credit > 0)
                return Result.Fail<Transaction>("A journal entry cannot have both a debit and a credit.");
            if (entry.Debit < 0 || entry.Credit < 0)
                return Result.Fail<Transaction>("Debit and credit amounts must be non-negative.");
        }

        var txnId = TransactionId.NewId();
        var transaction = new Transaction
        {
            Id = txnId,
            Timestamp = timestamp,
            Description = description,
            Status = TransactionStatus.Posted,
            RelatedOrderId = relatedOrderId
        };

        foreach (var entry in entries)
        {
            transaction._entries.Add(JournalEntry.Create(
                txnId, entry.AccountId, entry.Debit, entry.Credit, entry.Currency));
        }

        return Result.Ok(transaction);
    }

    public Result Reverse()
    {
        if (Status == TransactionStatus.Reversed)
            return Result.Fail("Transaction is already reversed.");
        Status = TransactionStatus.Reversed;
        return Result.Ok();
    }

    // Storage rehydration: builds a Transaction from already-validated row
    // data (status string, existing Id, ready-made entries) without re-running
    // Create's balance/sign checks. Repos and the LedgerService stitch use this;
    // domain code keeps using Create.
    public static Transaction Rehydrate(
        TransactionId id,
        DateTimeOffset timestamp,
        string description,
        TransactionStatus status,
        TradingOrderId? relatedOrderId,
        IEnumerable<JournalEntry> entries)
    {
        var transaction = new Transaction
        {
            Id = id,
            Timestamp = timestamp,
            Description = description,
            Status = status,
            RelatedOrderId = relatedOrderId
        };
        transaction._entries.AddRange(entries);
        return transaction;
    }
}
