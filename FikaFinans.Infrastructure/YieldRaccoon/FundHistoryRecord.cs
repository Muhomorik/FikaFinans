using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>FundHistoryRecords</c> time-series row,
/// copied from <c>YieldRaccoon.Domain.Entities.FundHistoryRecord</c> and trimmed
/// to the NAV date FikaFinans reads. One row per <c>(FundId, NavDate)</c>.
/// </summary>
/// <remarks>
/// <c>NavDate</c> is stored as ISO-8601 text (<c>YYYY-MM-DD</c>), so string
/// <c>MAX</c> orders chronologically — kept as <see cref="string"/> here to read
/// it without any DateOnly aggregate-translation concerns.
/// </remarks>
[DebuggerDisplay("FundHistoryRecord: {FundId} @ {NavDate} (Id {Id})")]
public sealed class FundHistoryRecord
{
    /// <summary>Auto-increment surrogate key (column <c>Id</c>).</summary>
    public long Id { get; set; }

    /// <summary>FK to <see cref="FundProfile.Isin"/> (column <c>FundId</c>).</summary>
    public string FundId { get; set; } = string.Empty;

    /// <summary>NAV calculation date, ISO-8601 <c>YYYY-MM-DD</c> (column <c>NavDate</c>).</summary>
    public string? NavDate { get; set; }

    /// <summary>Navigation back to the owning fund profile.</summary>
    public FundProfile? FundProfile { get; set; }
}
