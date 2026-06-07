using System.Diagnostics;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// The latest NAV slice for one fund as reported by YieldRacoon: its
/// <see cref="Isin"/>, the most recent trading date (<see cref="NavDate"/>),
/// the owning <see cref="CompanyName"/> used for the local company filter, and
/// the fund display <see cref="Name"/> (shown in the NAV Sync grid).
/// </summary>
/// <remarks>
/// <see cref="Name"/> defaults to empty so signal-only call sites (which only
/// need ISIN + date + company) stay terse; the YR provider always populates it.
/// </remarks>
[DebuggerDisplay("{Isin.Value,nq} {CompanyName,nq} @ {NavDate.Date,nq:yyyy-MM-dd}")]
public sealed record FundNavInfo(Isin Isin, DateTimeOffset NavDate, string CompanyName, string Name = "");

/// <summary>
/// Source seam for "latest NAV date per fund". The local implementation reads
/// YieldRacoon's read-only database (path from settings); the Azure
/// implementation will call YR's per-ISIN HTTP endpoint. The detection logic in
/// <see cref="NavChangeDetector"/> is identical regardless of source.
/// </summary>
/// <remarks>
/// Library code: implementations should use <c>ConfigureAwait(false)</c> and
/// honour cancellation at IO boundaries.
/// </remarks>
public interface ILatestNavProvider
{
    /// <summary>
    /// Return the latest NAV slice for every fund the provider knows about.
    /// </summary>
    /// <param name="ct">Cancels the underlying read.</param>
    /// <returns>One <see cref="FundNavInfo"/> per fund; never null, possibly empty.</returns>
    Task<IReadOnlyList<FundNavInfo>> GetLatestNavDatesAsync(CancellationToken ct = default);
}
