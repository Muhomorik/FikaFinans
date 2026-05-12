namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a chart-of-accounts entry. PartitionKey is the
/// constant <c>"accounts"</c> (single-portfolio assumption); RowKey is
/// the account <c>Code</c> which is already unique.
/// </summary>
/// <remarks>
/// <see cref="AccountId"/> keeps the domain Guid as an indexed,
/// non-key column so the rest of the bank-sim (which references
/// accounts by Guid) doesn't have to change.
/// </remarks>
public sealed class AccountEntity : TableEntity
{
    public Guid AccountId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Currency { get; init; } = "SEK";
}
