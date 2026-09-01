using System.Diagnostics;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only mirror of YieldRacoon's <c>FundProfiles</c> aggregate root, copied
/// from <c>YieldRaccoon.Domain.Entities.FundProfile</c>. Maps to the same
/// <c>FundProfiles</c> table in YR's SQLite database; we never write through it.
/// </summary>
/// <remarks>
/// <para>
/// Source: SemanticKernel-FundDocsQnA-dotnet-nextjs/YieldRaccoon. See that
/// repo's <c>docs/FUND-DATABASE-AGENT-GUIDE.md</c> for the full schema. Every
/// column of the real table is mirrored, so the two can be diffed directly when
/// the producer's schema moves.
/// </para>
/// <para>
/// Everything except <see cref="Isin"/> and <see cref="Name"/> is nullable in
/// YR, so consumers must supply their own fallbacks rather than assume a value.
/// </para>
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

    /// <summary>ISO 4217 currency the fund is priced in (column <c>CurrencyCode</c>).</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Producer's fund category, in the producer's own language (column <c>Category</c>).</summary>
    public string? Category { get; set; }

    /// <summary>Instrument type, e.g. <c>EQUITY_FUND</c> (column <c>FundType</c>).</summary>
    public string? FundType { get; set; }

    /// <summary>Whether the fund tracks an index (column <c>IsIndexFund</c>).</summary>
    public bool? IsIndexFund { get; set; }

    /// <summary><c>ACTIVE</c> or <c>PASSIVE</c> (column <c>ManagedType</c>).</summary>
    public string? ManagedType { get; set; }

    /// <summary>Total expense ratio in percent points — <c>2.17</c> means 2.17 % (column <c>TotalFee</c>).</summary>
    public decimal? TotalFee { get; set; }

    /// <summary>Management fee in percent points (column <c>ManagementFee</c>).</summary>
    public decimal? ManagementFee { get; set; }

    /// <summary>Producer's risk grade (column <c>Risk</c>).</summary>
    public int? Risk { get; set; }

    /// <summary>Producer's star rating (column <c>Rating</c>).</summary>
    public int? Rating { get; set; }

    /// <summary>Producer-computed Sharpe ratio, not recomputed here (column <c>SharpeRatio</c>).</summary>
    public decimal? SharpeRatio { get; set; }

    /// <summary>Producer-computed standard deviation, not recomputed here (column <c>StandardDeviation</c>).</summary>
    public decimal? StandardDeviation { get; set; }

    /// <summary>Suggested holding horizon, e.g. <c>FIVE_YEAR</c> (column <c>RecommendedHoldingPeriod</c>).</summary>
    public string? RecommendedHoldingPeriod { get; set; }

    /// <summary>Assets under management (column <c>Capital</c>).</summary>
    public decimal? Capital { get; set; }

    /// <summary>Number of holders at the producer (column <c>NumberOfOwners</c>).</summary>
    public int? NumberOfOwners { get; set; }

    /// <summary>
    /// Whether the fund can be bought at the producer (column <c>Buyable</c>).
    /// One of the filters the weekly CSV exports applied before this pipeline saw
    /// them; reading the database directly means applying it here instead.
    /// </summary>
    public bool? Buyable { get; set; }

    /// <summary>Producer's external orderbook identifier (column <c>OrderbookId</c>).</summary>
    public string? OrderbookId { get; set; }

    /// <summary>Fund inception date, ISO-8601 text at the producer (column <c>StartDate</c>).</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Whether the fund distributes dividends in cash (column <c>HasCashDividends</c>).</summary>
    public bool? HasCashDividends { get; set; }

    /// <summary>Whether a currency-exchange fee applies (column <c>HasCurrencyExchangeFee</c>).</summary>
    public bool? HasCurrencyExchangeFee { get; set; }

    /// <summary>Transaction fee in percent points (column <c>TransactionFee</c>).</summary>
    public decimal? TransactionFee { get; set; }

    /// <summary>Ongoing charges in percent points (column <c>OngoingFee</c>).</summary>
    public decimal? OngoingFee { get; set; }

    /// <summary>Minimum purchase amount in the fund's currency (column <c>MinimumBuy</c>).</summary>
    public decimal? MinimumBuy { get; set; }

    /// <summary><c>WORSE</c>, <c>AVERAGE</c> or <c>BETTER</c> (column <c>SustainabilityLevel</c>).</summary>
    public string? SustainabilityLevel { get; set; }

    /// <summary>Producer's sustainability rating (column <c>SustainabilityRating</c>).</summary>
    public int? SustainabilityRating { get; set; }

    /// <summary>Overall ESG score (column <c>EsgScore</c>).</summary>
    public decimal? EsgScore { get; set; }

    /// <summary>Environmental pillar score (column <c>EnvironmentalScore</c>).</summary>
    public decimal? EnvironmentalScore { get; set; }

    /// <summary>Social pillar score (column <c>SocialScore</c>).</summary>
    public decimal? SocialScore { get; set; }

    /// <summary>Governance pillar score (column <c>GovernanceScore</c>).</summary>
    public decimal? GovernanceScore { get; set; }

    /// <summary>Whether the producer flags the fund as low-carbon (column <c>LowCarbon</c>).</summary>
    public bool? LowCarbon { get; set; }

    /// <summary>EU SFDR classification — Article 6, 8 or 9 (column <c>EuArticleType</c>).</summary>
    public string? EuArticleType { get; set; }

    /// <summary>When the producer's crawler first saw this fund (column <c>FirstSeenAt</c>).</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>When the producer's crawler last refreshed this row (column <c>CrawlerLastUpdatedAt</c>).</summary>
    public DateTimeOffset? CrawlerLastUpdatedAt { get; set; }

    /// <summary>When the producer last visited the fund's about page (column <c>AboutFundLastVisitedAt</c>).</summary>
    public DateTimeOffset? AboutFundLastVisitedAt { get; set; }

    /// <summary>Fund description text from the producer's fund-reference API (column <c>Description</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Time-series history rows for this fund (one per NAV date).</summary>
    public ICollection<FundHistoryRecord> HistoryRecords { get; set; } = new List<FundHistoryRecord>();
}
