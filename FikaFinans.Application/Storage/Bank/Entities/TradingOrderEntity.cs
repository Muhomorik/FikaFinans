namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a single trading order. PartitionKey is
/// <c>"orders/{yyyy-MM-dd}"</c> (date the order was created); RowKey is
/// <c>"{isin}/{side}"</c> for daily idempotency — a second order for the
/// same ISIN+side on the same day overwrites the first (last-write-wins).
/// </summary>
/// <remarks>
/// Domain identifiers (<see cref="OrderId"/>, <see cref="FundId"/>) stay
/// as indexed Guid columns so service-layer code that holds those Guids
/// keeps working. <see cref="Isin"/> is denormalised onto the row so
/// the RowKey can be reconstructed without a fund-table join.
/// </remarks>
public sealed class TradingOrderEntity : TableEntity
{
    public Guid OrderId { get; init; }
    public Guid FundId { get; init; }
    public string Isin { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public decimal AmountValue { get; init; }
    public string Currency { get; init; } = "SEK";
    public decimal? Units { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SettledAt { get; init; }
    public decimal? SettlementNavPerUnit { get; init; }
    public decimal? SettledUnits { get; init; }
}
