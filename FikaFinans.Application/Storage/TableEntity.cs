namespace FikaFinans.Application.Storage;

/// <summary>
/// Base shape for every row stored through the Tables-shaped repository
/// layer: a (PartitionKey, RowKey) primary key and nothing else. Concrete
/// entities add their own columns as init-only properties.
/// </summary>
/// <remarks>
/// Mirrors Azure Tables row identity. PartitionKey scopes a partition
/// scan; RowKey is the natural key inside that partition. There are no
/// foreign keys, no navigation properties, no ETag — last-write-wins is
/// the only supported concurrency model.
/// </remarks>
public abstract class TableEntity
{
    public string PartitionKey { get; init; } = string.Empty;
    public string RowKey { get; init; } = string.Empty;
}
