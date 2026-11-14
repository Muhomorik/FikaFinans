using System.Diagnostics;

using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 10's view of the portfolio: the holdings, cash and layers, read at run time through
/// the seams step 1 uses, and laid over the step 9 records. No progress row carries cash or
/// frozen positions, and a record's held and value fields are only as fresh as its fund's last
/// step 1 — a trade settled since would not show.
/// </summary>
/// <remarks>
/// Read-only — reading never changes the holdings, the structure or the records given.
/// </remarks>
public interface IStep10PortfolioReader
{
    /// <summary>
    /// Reads the holdings and the pinned layers, and returns <paramref name="records"/> with
    /// their held, value, cost basis and layer taken from them, beside the portfolio-wide
    /// fields no record carries.
    /// </summary>
    Task<Step10Portfolio> ReadAsync(IReadOnlyList<FundRecord> records, CancellationToken ct = default);
}

/// <summary>What one read of the portfolio found — the agent's input beside the config.</summary>
/// <param name="Funds">
/// The records, in the order given, with <see cref="FundRecord.CurrentlyHeld"/>,
/// <see cref="FundRecord.CurrentValueKr"/>, <see cref="FundRecord.CostBasisKr"/> and
/// <see cref="FundRecord.Layer"/> from the holdings and the structure; every other field as
/// given. A record pinned <c>writeoff</c> is left out — never traded.
/// </param>
/// <param name="FrozenPositions">Holdings in a fund pinned <c>writeoff</c>, in the holdings' order.</param>
/// <param name="CashAvailableKr">The holdings' cash.</param>
/// <param name="HeldWithoutRecord">
/// Holdings that are neither frozen nor backed by a record — a fund that keeps failing, or
/// one outside the company filter. Not in <see cref="Funds"/>, so not in the portfolio value;
/// whether step 10 should act on them is open on its page.
/// </param>
[DebuggerDisplay("{Funds.Count} funds, {FrozenPositions.Count} frozen, cash {CashAvailableKr}, {HeldWithoutRecord.Count} held without record")]
public sealed record Step10Portfolio(
    IReadOnlyList<FundRecord> Funds,
    IReadOnlyList<FrozenPosition> FrozenPositions,
    decimal CashAvailableKr,
    IReadOnlyList<Position> HeldWithoutRecord);
