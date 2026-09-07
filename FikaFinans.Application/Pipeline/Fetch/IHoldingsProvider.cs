using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Fetch;

/// <summary>
/// Source seam for the portfolio's holdings and its cash. Sibling of
/// <see cref="IFundMetadataProvider"/>, <see cref="IFundSummaryProvider"/> and
/// <see cref="IFundSnapshotProvider"/> — the only one reading FikaFinans' own bank store
/// rather than the producer's data, hence no fund, company or week to scope by.
/// </summary>
/// <remarks>
/// Read-only on purpose. Position changes are a consequence of orders settling, which
/// <c>ITradingService</c> owns together with the matching ledger entries; both paths meet
/// again at <c>IPositionsRepository</c>, so the storage backend still swaps in one place.
/// </remarks>
public interface IHoldingsProvider
{
    /// <summary>
    /// Reads every held position plus portfolio-level cash. Portfolio-scoped, not per
    /// fund: cash, the frozen list and the downstream weight calculations are all sums
    /// over the whole book, so a single fund's slice cannot stand in for it.
    /// </summary>
    Task<PositionsParseResult> GetHoldingsAsync(CancellationToken ct = default);
}
