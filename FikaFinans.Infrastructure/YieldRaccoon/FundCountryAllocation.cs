using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>FundCountryAllocations</c> row, copied
/// from <c>YieldRaccoon.Domain.Entities.FundCountryAllocation</c>. One row per
/// <c>(Isin, CountryId)</c>; FikaFinans never writes through it.
/// </summary>
/// <remarks>
/// Latest-only at the producer — re-ingested on every crawl, no history retained.
/// Like the producer, this mirror declares no navigation property back to
/// <see cref="FundProfile"/>: queries go through the allocation table directly.
/// </remarks>
[DebuggerDisplay("FundCountryAllocation: {Isin} → {CountryId} = {Percentage}%")]
public sealed class FundCountryAllocation
{
    /// <summary>Primary key (column <c>FundCountryAllocationId</c>, GUID).</summary>
    public Guid FundCountryAllocationId { get; set; }

    /// <summary>FK to <see cref="FundProfile.Isin"/> (column <c>Isin</c>).</summary>
    public string Isin { get; set; } = string.Empty;

    /// <summary>FK to <see cref="Country.CountryId"/> (column <c>CountryId</c>).</summary>
    public Guid CountryId { get; set; }

    /// <summary>Share of the fund's portfolio in this country, 0–100 (column <c>Percentage</c>).</summary>
    public decimal Percentage { get; set; }
}
