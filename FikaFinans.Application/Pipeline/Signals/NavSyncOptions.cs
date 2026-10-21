using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Construction-time options for the local NAV-sync simulation. Built from
/// <c>AppSettings</c> at DI composition (like <see cref="StreamingPipelineOptions"/>).
/// </summary>
public sealed record NavSyncOptions
{
    /// <summary>
    /// Asset-manager / company name detection is restricted to (matched against
    /// <see cref="FikaFinans.Application.Pipeline.Signals.FundNavInfo.CompanyName"/>,
    /// case-insensitive). Ctor-only by design: we do not run the full universe
    /// locally — this narrows detection to one company. Empty disables the
    /// company filter (all funds considered).
    /// </summary>
    public string CompanyFilter { get; init; } = string.Empty;

    /// <summary>
    /// Whether a fund run by <paramref name="company"/> is in the universe — the one place the
    /// <see cref="CompanyFilter"/> rule lives, so the NAV sync and step 9's barrier agree.
    /// </summary>
    public bool MatchesCompany(Company company)
        => string.IsNullOrWhiteSpace(CompanyFilter)
           || string.Equals(company.Value, CompanyFilter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Filesystem path to YieldRacoon's local database, opened <b>read-only</b>
    /// by <c>YieldRaccoonSqliteNavProvider</c> to read the latest NAV date per
    /// ISIN. Empty until configured in Settings — the provider then returns no
    /// funds (a no-op), so detection raises nothing.
    /// </summary>
    public string YieldRaccoonDbPath { get; init; } = string.Empty;
}
