using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>Sectors</c> lookup row, copied from
/// <c>YieldRaccoon.Domain.Entities.Sector</c>. Referenced by
/// <see cref="FundSectorAllocation"/>; FikaFinans never writes through it.
/// </summary>
/// <remarks>
/// <see cref="DisplayName"/> is the natural key at the producer (unique-indexed)
/// and carries the source's Swedish naming — "Teknik", "Råvaror", "Industri".
/// </remarks>
[DebuggerDisplay("Sector: {DisplayName}")]
public sealed class Sector
{
    /// <summary>Primary key (column <c>SectorId</c>, GUID).</summary>
    public Guid SectorId { get; set; }

    /// <summary>Name as it appears in the producer's payload (column <c>DisplayName</c>, unique).</summary>
    public string DisplayName { get; set; } = string.Empty;
}
