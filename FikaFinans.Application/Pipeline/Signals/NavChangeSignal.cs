using System.Diagnostics;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// A NAV-change signal: the environment-agnostic event that fund
/// <see cref="Isin"/> has a new trading-date NAV (<see cref="NavDate"/>) worth
/// processing. Mirrors the queue message in backend-nav-sync-plan.md — a
/// <em>signal, not a payload</em>; the fund data is fetched out of band when
/// the pipeline runs. Locally it travels over an Rx stream; in Azure it becomes
/// a Queue Storage message.
/// </summary>
[DebuggerDisplay("{Isin.Value,nq} @ {NavDate.Date,nq:yyyy-MM-dd}")]
public sealed record NavChangeSignal(Isin Isin, DateTimeOffset NavDate);
