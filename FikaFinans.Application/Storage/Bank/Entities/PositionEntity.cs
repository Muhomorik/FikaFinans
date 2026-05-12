namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for a held position. PartitionKey is the constant
/// <c>"positions"</c> (single-portfolio assumption); RowKey is the ISIN,
/// or the literal <c>"CASH"</c> for the single cash pseudo-row.
/// </summary>
/// <remarks>
/// Replaces the <c>FundHolding</c> EF entity and <c>positions.csv</c> as
/// the canonical source of holdings. Cost basis equals current value on
/// the cash row by convention; <see cref="Units"/> /
/// <see cref="AvgCostPerUnit"/> are zero on the cash row.
/// <see cref="Source"/> tracks provenance of the latest write —
/// <c>"manual"</c>, <c>"sendToBank"</c>, or <c>"reconciled"</c>.
/// </remarks>
public sealed class PositionEntity : TableEntity
{
    public string Isin { get; init; } = string.Empty;
    public string? Name { get; init; }
    public decimal CurrentValueKr { get; init; }
    public decimal CostBasisKr { get; init; }
    // Unit-level state needed by the bank-sim's sell flow. Carried
    // beyond what positions.csv stores so the trading engine can validate
    // "sell N units" and compute cost-basis-of-sold-units without round-
    // tripping through NAV. AvgCostPerUnit is preserved across sells
    // (matches the retired FundHolding semantics).
    public decimal Units { get; init; }
    public decimal AvgCostPerUnit { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
    public string Source { get; init; } = string.Empty;
}
