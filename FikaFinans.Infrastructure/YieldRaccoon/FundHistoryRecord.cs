using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>FundHistoryRecords</c> time-series row,
/// copied from <c>YieldRaccoon.Domain.Entities.FundHistoryRecord</c>. One row per
/// <c>(FundId, NavDate)</c> — the raw daily series the producer's bucket
/// statistics are derived from.
/// </summary>
/// <remarks>
/// <c>NavDate</c> is stored as ISO-8601 text (<c>YYYY-MM-DD</c>), so string
/// <c>MAX</c> orders chronologically — kept as <see cref="string"/> here to read
/// it without any DateOnly aggregate-translation concerns.
/// </remarks>
[DebuggerDisplay("FundHistoryRecord: {FundId} @ {NavDate} = {Nav} (Id {Id})")]
public sealed class FundHistoryRecord
{
    /// <summary>Auto-increment surrogate key (column <c>Id</c>).</summary>
    public long Id { get; set; }

    /// <summary>FK to <see cref="FundProfile.Isin"/> (column <c>FundId</c>).</summary>
    public string FundId { get; set; } = string.Empty;

    /// <summary>NAV calculation date, ISO-8601 <c>YYYY-MM-DD</c> (column <c>NavDate</c>).</summary>
    public string? NavDate { get; set; }

    /// <summary>Net asset value per unit on <see cref="NavDate"/> (column <c>Nav</c>).</summary>
    public decimal? Nav { get; set; }

    /// <summary>Assets under management at the time of recording (column <c>Capital</c>).</summary>
    public decimal? Capital { get; set; }

    /// <summary>Number of holders at the time of recording (column <c>NumberOfOwners</c>).</summary>
    public int? NumberOfOwners { get; set; }

    /// <summary>Producer's risk grade at the time of recording (column <c>Risk</c>).</summary>
    public int? Risk { get; set; }

    /// <summary>Producer-computed Sharpe ratio at the time of recording (column <c>SharpeRatio</c>).</summary>
    public decimal? SharpeRatio { get; set; }

    /// <summary>Producer-computed standard deviation at the time of recording (column <c>StandardDeviation</c>).</summary>
    public decimal? StandardDeviation { get; set; }

    /// <summary>Navigation back to the owning fund profile.</summary>
    public FundProfile? FundProfile { get; set; }
}
