using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Fetch;

/// <summary>
/// Source seam for one fund's rolling-horizon snapshot — the 12-week and 1-year
/// aggregates at a single evaluation date, as the producer's snapshot export
/// carries them. Sibling of <see cref="IFundMetadataProvider"/> and
/// <see cref="IFundSummaryProvider"/>, which cover identity and the 2-week buckets.
/// </summary>
public interface IFundSnapshotProvider
{
    /// <summary>
    /// Reads one fund's snapshot as of the given week. Null when the source holds
    /// no NAV rows at all; a fund with too little history instead comes back with
    /// the affected metrics null. Sources without per-week exports ignore
    /// <paramref name="isoWeek"/>.
    /// </summary>
    Task<FundSnapshot?> GetSnapshotAsync(
        Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default);
}
