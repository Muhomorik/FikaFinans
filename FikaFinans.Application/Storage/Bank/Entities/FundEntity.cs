namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a fund. PartitionKey is the constant
/// <c>"funds"</c> (single-portfolio assumption); RowKey is the ISIN.
/// </summary>
/// <remarks>
/// <see cref="FundId"/> survives as an indexed non-key column so the
/// bank-sim's Guid-based service surface (<c>TradingOrder.FundId</c>
/// etc.) keeps working. NAV history is stored separately as
/// <see cref="NavSnapshotEntity"/> rows in partition <c>"nav/{isin}"</c>.
/// </remarks>
public sealed class FundEntity : TableEntity
{
    public Guid FundId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Isin { get; init; } = string.Empty;
    public string Currency { get; init; } = "SEK";
}
