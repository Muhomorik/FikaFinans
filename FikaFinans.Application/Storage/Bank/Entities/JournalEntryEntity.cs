namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a single debit/credit line. Sits in the same
/// partition as its parent <see cref="TransactionEntity"/> so a single
/// partition scan returns the transaction header plus all its lines.
/// RowKey is the <see cref="JournalEntryId"/> Guid as a string.
/// </summary>
/// <remarks>
/// <see cref="TransactionId"/> and <see cref="AccountId"/> are stored as
/// regular indexed Guid columns — there are no foreign keys, no cascade
/// deletes, no navigation properties. The link back to the parent
/// transaction is data, not a relationship.
/// </remarks>
public sealed class JournalEntryEntity : TableEntity
{
    public Guid JournalEntryId { get; init; }
    public Guid TransactionId { get; init; }
    public Guid AccountId { get; init; }
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public string Currency { get; init; } = "SEK";
}
