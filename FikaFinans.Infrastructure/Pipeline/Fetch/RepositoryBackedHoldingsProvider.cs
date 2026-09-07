using System.Diagnostics;

using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Fetch;

/// <summary>
/// <see cref="IHoldingsProvider"/> backed by <see cref="IPositionsRepository"/>. The
/// storage backend swaps underneath it — SQLite today, the in-memory double in tests,
/// Azure Tables once the cloud store lands — without this class changing.
/// </summary>
/// <remarks>
/// The mapping is the one <c>DataLoaderAgent.ToPositionsParseResult</c> already performs
/// for the CSV-backed path, kept identical so both routes hand the agent the same shape
/// and the same warning text.
/// </remarks>
public sealed class RepositoryBackedHoldingsProvider : IHoldingsProvider
{
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly ILogger _logger;
    private readonly IPositionsRepository _positions;

    public RepositoryBackedHoldingsProvider(ILogger logger, IPositionsRepository positions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
    }

    /// <inheritdoc />
    public async Task<PositionsParseResult> GetHoldingsAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var rows = await _positions.QueryPartitionAsync(PositionsPartition, ct).ConfigureAwait(false);
        var result = ToPositionsParseResult(rows);

        stopwatch.Stop();
        _logger.Trace(
            "Holdings read done — {0} holding(s), cash={1}, {2} ms",
            result.Holdings.Count, result.CashAvailableKr, stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// Adapter from repo rows to <see cref="PositionsParseResult"/>. Cash row is split out
    /// by RowKey; holdings preserve the CSV shape
    /// (<c>Isin</c>/<c>Name</c>/<c>CurrentValueKr</c>/<c>CostBasisKr</c>) and the
    /// unit-level fields ride along on the row but aren't surfaced here — they're a
    /// bank-sim concern.
    /// </summary>
    private static PositionsParseResult ToPositionsParseResult(IReadOnlyList<PositionEntity> rows)
    {
        var warnings = new List<string>();
        var cashRow = rows.FirstOrDefault(r => r.RowKey == CashRowKey);
        decimal cashAvailable = cashRow?.CurrentValueKr ?? 0m;
        if (cashRow is null)
            warnings.Add("Positions repo has no CASH row; cash_available_kr defaults to 0.");

        var holdings = rows
            .Where(r => r.RowKey != CashRowKey)
            .Select(r => new Position
            {
                Isin = new Isin(r.Isin),
                Name = r.Name,
                CurrentValueKr = r.CurrentValueKr,
                CostBasisKr = r.CostBasisKr,
            })
            .ToList();

        return new PositionsParseResult
        {
            Holdings = holdings,
            CashAvailableKr = cashAvailable,
            Warnings = warnings,
            TotalRowCount = rows.Count,
        };
    }
}
