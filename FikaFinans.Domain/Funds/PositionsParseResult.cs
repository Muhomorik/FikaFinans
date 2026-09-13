namespace FikaFinans.Domain.Funds;

public sealed class PositionsParseResult
{
    public required IReadOnlyList<Position> Holdings { get; init; }
    public required decimal CashAvailableKr { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required int TotalRowCount { get; init; }

    /// <summary>
    /// An empty book — no holdings, no cash, nothing read. What a field holds before a fetch
    /// fills it, and what a parser returns for a source with no rows.
    /// </summary>
    /// <remarks>Shared: every property is init-only and the lists are empty singletons.</remarks>
    public static PositionsParseResult Empty { get; } = new()
    {
        Holdings = Array.Empty<Position>(),
        CashAvailableKr = 0m,
        Warnings = Array.Empty<string>(),
        TotalRowCount = 0,
    };
}
