namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a ledger transaction header. PartitionKey is
/// <c>"ledger/{yyyy-MM}"</c> (month bucket from <see cref="Timestamp"/>);
/// RowKey is the <see cref="TransactionId"/> Guid as a string.
/// </summary>
/// <remarks>
/// The <c>Entries</c> collection that lived on the domain aggregate does
/// NOT exist here — journal entries are a separate row set queried via
/// <see cref="IJournalEntriesRepository"/>. Service-layer code stitches
/// them together with a two-reads + in-memory join.
/// </remarks>
public sealed class TransactionEntity : TableEntity
{
    public Guid TransactionId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public Guid? RelatedOrderId { get; init; }
}
