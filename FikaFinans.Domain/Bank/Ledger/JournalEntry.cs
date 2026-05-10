using System.Diagnostics;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;

namespace FikaFinans.Domain.Bank.Ledger;

[DebuggerDisplay("{AccountId}: Dr {DebitAmount} Cr {CreditAmount} {Currency}")]
public class JournalEntry
{
    public JournalEntryId Id { get; private init; }
    public TransactionId TransactionId { get; private init; }
    public AccountId AccountId { get; private init; }
    public decimal DebitAmount { get; private init; }
    public decimal CreditAmount { get; private init; }
    public string Currency { get; private init; } = "SEK";

    private JournalEntry() { }

    internal static JournalEntry Create(
        TransactionId transactionId,
        AccountId accountId,
        decimal debitAmount,
        decimal creditAmount,
        string currency)
    {
        return new JournalEntry
        {
            Id = JournalEntryId.NewId(),
            TransactionId = transactionId,
            AccountId = accountId,
            DebitAmount = debitAmount,
            CreditAmount = creditAmount,
            Currency = currency
        };
    }

    public Money GetDebit() => new(DebitAmount, Currency);
    public Money GetCredit() => new(CreditAmount, Currency);
}
