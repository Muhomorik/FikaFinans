namespace FikaFinans.Infrastructure.Storage.Sqlite.Entities;

/// <summary>
/// EF row for the <c>Positions</c> table — the SQLite-side counterpart of
/// <see cref="FikaFinans.Application.Storage.Bank.Entities.PositionEntity"/>.
/// Composite primary key <c>(PartitionKey, RowKey)</c> mirrors Tables row
/// identity. Pure data, no FKs, no nav props.
/// </summary>
public sealed class PositionRow
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    public string Isin { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal CurrentValueKr { get; set; }
    public decimal CostBasisKr { get; set; }
    public decimal Units { get; set; }
    public decimal AvgCostPerUnit { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public string Source { get; set; } = string.Empty;
}
