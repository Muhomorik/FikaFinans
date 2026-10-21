using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Step 9's barrier: whether every fund in the universe is through steps 1–8, read off the
/// progress rows. Step 9 needs the whole universe, and under signal-driven runs nothing
/// else says when the last fund got there.
/// </summary>
/// <remarks>
/// Read-only — checking never changes a row, so every trigger can ask as often as it likes.
/// </remarks>
public interface IStep09Barrier
{
    /// <summary>Reads the universe and every fund's row, and says whether the barrier has closed.</summary>
    Task<Step09BarrierState> CheckAsync(CancellationToken ct = default);
}

/// <summary>What one barrier check found.</summary>
/// <param name="Universe">
/// Every fund matching <see cref="NavSyncOptions.CompanyFilter"/>, in the NAV source's order —
/// the funds step 9 reads, whether or not each has a record.
/// </param>
/// <param name="Failed">
/// Funds whose run failed on their latest trading date. Finished, so they do not hold the
/// barrier, but with no step 8 record they drop out of the pass.
/// </param>
/// <param name="Holding">Funds not through steps 1–8 yet, and what their row says.</param>
[DebuggerDisplay("Closed = {IsClosed}, {Universe.Count} funds, {Failed.Count} failed, {Holding.Count} holding")]
public sealed record Step09BarrierState(
    IReadOnlyList<Isin> Universe,
    IReadOnlyList<Isin> Failed,
    IReadOnlyList<Step09BarrierHold> Holding)
{
    /// <summary>
    /// No fund holds the barrier. True for an empty universe too — whether a pass over no
    /// funds is worth running is the caller's call.
    /// </summary>
    public bool IsClosed => Holding.Count == 0;
}

/// <summary>A fund keeping step 9's barrier open.</summary>
/// <param name="Isin">The fund.</param>
/// <param name="Status">
/// <see cref="NavSyncStatusKind.Processing"/> while its run is going;
/// <see cref="NavSyncStatusKind.New"/> or <see cref="NavSyncStatusKind.Changed"/> while its
/// latest trading date has not been run.
/// </param>
[DebuggerDisplay("{Isin.Value,nq} {Status}")]
public sealed record Step09BarrierHold(Isin Isin, NavSyncStatusKind Status);
