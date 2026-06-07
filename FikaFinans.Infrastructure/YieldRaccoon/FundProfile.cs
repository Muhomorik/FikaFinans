using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>FundProfiles</c> aggregate root, copied
/// from <c>YieldRaccoon.Domain.Entities.FundProfile</c> and trimmed to the
/// columns FikaFinans reads (ISIN + display name + company). Maps to the same
/// <c>FundProfiles</c> table in YR's SQLite database; we never write through it.
/// </summary>
/// <remarks>
/// Source: SemanticKernel-FundDocsQnA-dotnet-nextjs/YieldRaccoon. See that
/// repo's <c>docs/FUND-DATABASE-AGENT-GUIDE.md</c> for the full schema.
/// </remarks>
[DebuggerDisplay("FundProfile: {Isin} {Name} ({CompanyName})")]
public sealed class FundProfile
{
    /// <summary>ISO 6166 fund identifier — primary key (column <c>Isin</c>).</summary>
    public string Isin { get; set; } = string.Empty;

    /// <summary>Fund display name (column <c>Name</c>, non-null in YR).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Fund management company / asset manager (column <c>CompanyName</c>).</summary>
    public string? CompanyName { get; set; }

    /// <summary>Time-series history rows for this fund (one per NAV date).</summary>
    public ICollection<FundHistoryRecord> HistoryRecords { get; set; } = new List<FundHistoryRecord>();
}
