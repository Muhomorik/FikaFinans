namespace FikaFinans.Domain.Funds;

public sealed class PositionsParseResult
{
    public required IReadOnlyList<Position> Holdings { get; init; }
    public required decimal CashAvailableKr { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required int TotalRowCount { get; init; }
}
