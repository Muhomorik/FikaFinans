namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a single NAV-per-unit observation. PartitionKey
/// is <c>"nav/{isin}"</c> so each fund's history is one partition scan;
/// RowKey is the ISO 8601 round-trip timestamp of <see cref="Date"/>,
/// which keeps rows lexically sortable inside the partition.
/// </summary>
public sealed class NavSnapshotEntity : TableEntity
{
    public Guid NavSnapshotId { get; init; }
    public Guid FundId { get; init; }
    public string Isin { get; init; } = string.Empty;
    public DateTimeOffset Date { get; init; }
    public decimal NavPerUnit { get; init; }
}
