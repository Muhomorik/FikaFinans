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
/// Read-model value object returned by <see cref="ILatestNavProvider"/>.
/// <see cref="Name"/> defaults to empty so signal-only call sites (which only
/// need ISIN + date + company) stay terse; the YR provider always populates it.
/// </remarks>
[DebuggerDisplay("{Isin.Value,nq} {CompanyName,nq} @ {NavDate.Date,nq:yyyy-MM-dd}")]
public sealed record FundNavInfo(Isin Isin, DateTimeOffset NavDate, string CompanyName, string Name = "");