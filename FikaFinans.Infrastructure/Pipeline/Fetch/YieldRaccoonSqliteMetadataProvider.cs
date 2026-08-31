using System.Diagnostics;

using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.YieldRaccoon;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Fetch;

/// <summary>
/// Local <see cref="IFundMetadataProvider"/> that reads one fund's profile row
/// from YieldRacoon's database via EF Core, opened <b>read-only</b>. The local
/// stand-in for YR's per-ISIN HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Uses the copied read-only ORM model (<see cref="YieldRaccoonReadDbContext"/>)
/// mapping YR's <c>FundProfiles</c> table. That table holds <b>current state
/// only</b> — one row per fund, no history — so <c>isoWeek</c> is accepted for
/// contract compatibility and ignored. A caller needing a genuine per-week
/// snapshot has to read the producer's weekly CSV exports instead.
/// </para>
/// <para>
/// Library code: awaits with <c>ConfigureAwait(false)</c> and honours
/// cancellation at IO boundaries.
/// </para>
/// </remarks>
public sealed class YieldRaccoonSqliteMetadataProvider : IFundMetadataProvider
{
    private readonly ILogger _logger;
    private readonly string _dbPath;

    public YieldRaccoonSqliteMetadataProvider(ILogger logger, NavSyncOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _dbPath = options.YieldRaccoonDbPath;
    }

    /// <inheritdoc />
    public async Task<FundMetadata?> GetMetadataAsync(
        Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default)
    {
        // No path configured yet (Settings not filled in) → no metadata, so the
        // caller treats the fund as absent rather than throwing on a missing file.
        if (string.IsNullOrWhiteSpace(_dbPath))
        {
            _logger.Trace("YR metadata read skipped — no database path configured");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.Trace("YR metadata read starting — isin={0} db={1}", isin.Value, _dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            // Don't hold a pooled handle on YR's file between reads.
            Pooling = false
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<YieldRaccoonReadDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using var db = new YieldRaccoonReadDbContext(options);

        var profile = await db.FundProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(fp => fp.Isin == isin.Value, ct)
            .ConfigureAwait(false);

        stopwatch.Stop();

        if (profile is null)
        {
            _logger.Trace("YR metadata read done — isin={0} not found, {1} ms", isin.Value, stopwatch.ElapsedMilliseconds);
            return null;
        }

        // The export filename's company token is lower-cased while CompanyName
        // preserves original case, so this match is deliberately case-insensitive.
        if (!string.Equals(profile.CompanyName, company.Value, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Trace(
                "YR metadata read done — isin={0} belongs to '{1}', not '{2}'; treated as out of scope",
                isin.Value, profile.CompanyName, company.Value);
            return null;
        }

        _logger.Trace("YR metadata read done — isin={0} mapped in {1} ms", isin.Value, stopwatch.ElapsedMilliseconds);

        return Map(profile);
    }

    /// <summary>
    /// Projects a YR profile row onto <see cref="FundMetadata"/>. Almost every YR
    /// column is nullable while most of the target's are required, so the string
    /// fields fall back to empty and the non-nullable numerics to zero.
    /// </summary>
    /// <remarks>
    /// <see cref="FundMetadata.SharpeRatioStatic"/> and
    /// <see cref="FundMetadata.StandardDeviationStatic"/> stay nullable on
    /// purpose: a missing value there means insufficient data or the producer's
    /// volatility guard, and must never become zero.
    /// </remarks>
    private static FundMetadata Map(FundProfile profile) => new()
    {
        Isin = new Isin(profile.Isin),
        Name = profile.Name,
        CompanyName = profile.CompanyName ?? string.Empty,
        CurrencyCode = profile.CurrencyCode ?? string.Empty,
        Category = profile.Category ?? string.Empty,
        FundType = profile.FundType ?? string.Empty,
        IsIndexFund = profile.IsIndexFund,
        ManagedType = profile.ManagedType ?? string.Empty,
        TotalFee = profile.TotalFee ?? 0m,
        ManagementFee = profile.ManagementFee ?? 0m,
        Risk = profile.Risk,
        Rating = profile.Rating,
        SharpeRatioStatic = profile.SharpeRatio,
        StandardDeviationStatic = profile.StandardDeviation,
        RecommendedHoldingPeriod = profile.RecommendedHoldingPeriod ?? string.Empty,
        Capital = profile.Capital ?? 0m,
        NumberOfOwners = profile.NumberOfOwners ?? 0,
    };
}
