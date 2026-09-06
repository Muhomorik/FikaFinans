using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Fetch;

/// <summary>
/// Source seam for one fund's rolling two-week NAV buckets — the same content as
/// the producer's summary export, in object form. Sibling of
/// <see cref="IFundMetadataProvider"/>, implemented once per source (weekly CSV,
/// SQLite, HTTP backend). Metadata decides whether a fund exists downstream;
/// buckets only say how much NAV history it has.
/// </summary>
public interface IFundSummaryProvider
{
    /// <summary>
    /// Reads one fund's NAV buckets as of the given week, oldest window first.
    /// Never null — empty means no rows, out of scope, or a series too short to
    /// fill one window. Sources without per-week exports ignore
    /// <paramref name="isoWeek"/>.
    /// </summary>
    Task<IReadOnlyList<NavBucket>> GetNavBucketsAsync(
        Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default);
}
