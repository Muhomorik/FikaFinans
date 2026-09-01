using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>FundSectorAllocations</c> row, copied
/// from <c>YieldRaccoon.Domain.Entities.FundSectorAllocation</c>. One row per
/// <c>(Isin, SectorId)</c>; FikaFinans never writes through it.
/// </summary>
/// <remarks>
/// Latest-only at the producer — re-ingested on every crawl, no history retained.
/// Like the producer, this mirror declares no navigation property back to
/// <see cref="FundProfile"/>: queries go through the allocation table directly.
/// </remarks>
[DebuggerDisplay("FundSectorAllocation: {Isin} → {SectorId} = {Percentage}%")]
public sealed class FundSectorAllocation
{
    /// <summary>Primary key (column <c>FundSectorAllocationId</c>, GUID).</summary>
    public Guid FundSectorAllocationId { get; set; }

    /// <summary>FK to <see cref="FundProfile.Isin"/> (column <c>Isin</c>).</summary>
    public string Isin { get; set; } = string.Empty;

    /// <summary>FK to <see cref="Sector.SectorId"/> (column <c>SectorId</c>).</summary>
    public Guid SectorId { get; set; }

    /// <summary>Share of the fund's portfolio in this sector, 0–100 (column <c>Percentage</c>).</summary>
    public decimal Percentage { get; set; }
}
