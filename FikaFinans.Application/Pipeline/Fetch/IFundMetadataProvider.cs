using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Fetch;

/// <summary>
/// Source seam for one fund's metadata — the static per-fund facts (name, fee,
/// category, risk, owner count) that form the spine of a
/// <see cref="FundRecord"/>. No metadata for an ISIN means that fund does not
/// exist downstream.
/// </summary>
/// <remarks>
/// <para>
/// Declared in the Application layer and implemented in Infrastructure once per
/// source — the producer's weekly CSV exports, its SQLite database, and its HTTP
/// backend. Callers are identical regardless of which is registered, the same
/// asymmetry <see cref="ILatestNavProvider"/> already carries for NAV dates.
/// </para>
/// <para>
/// Library code: implementations should use <c>ConfigureAwait(false)</c> and
/// honour cancellation at IO boundaries.
/// </para>
/// </remarks>
public interface IFundMetadataProvider
{
    /// <summary>
    /// Reads one fund's metadata as of the given week.
    /// </summary>
    /// <param name="isin">The fund to read. Must not be default.</param>
    /// <param name="company">
    /// The company whose funds are in scope. Must not be default. File-backed
    /// sources locate their export by it; others use it to filter.
    /// </param>
    /// <param name="isoWeek">
    /// The week to read. Must not be default. Only sources that retain
    /// per-week snapshots can honour it — a source backed by the producer's
    /// current-state profile table returns today's values whatever week is
    /// asked for.
    /// </param>
    /// <param name="ct">Cancels the underlying read.</param>
    /// <returns>
    /// The fund's metadata, or <c>null</c> when the source holds no row for it —
    /// excluded by the source's filters, or absent from that week. Null is an
    /// ordinary outcome, not an error.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was signalled during the read.
    /// </exception>
    Task<FundMetadata?> GetMetadataAsync(Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default);
}
