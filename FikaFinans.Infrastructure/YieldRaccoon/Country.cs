using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>Countries</c> lookup row, copied from
/// <c>YieldRaccoon.Domain.Entities.Country</c>. Referenced by
/// <see cref="FundCountryAllocation"/>; FikaFinans never writes through it.
/// </summary>
/// <remarks>
/// <see cref="DisplayName"/> is the natural key at the producer (unique-indexed)
/// and carries the source's Swedish naming — "USA", "Kanada", "Tyskland".
/// </remarks>
[DebuggerDisplay("Country: {DisplayName} ({CountryCode})")]
public sealed class Country
{
    /// <summary>Primary key (column <c>CountryId</c>, GUID).</summary>
    public Guid CountryId { get; set; }

    /// <summary>Name as it appears in the producer's payload (column <c>DisplayName</c>, unique).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 code, or <c>null</c> when the source omits it (column <c>CountryCode</c>).</summary>
    public string? CountryCode { get; set; }
}
