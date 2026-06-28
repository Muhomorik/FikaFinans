using System.Diagnostics;
using System.Globalization;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Infrastructure.YieldRaccoon;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Signals;

/// <summary>
/// Local <see cref="ILatestNavProvider"/> that reads the latest NAV date +
/// company per ISIN from YieldRacoon's database via EF Core, opened
/// <b>read-only</b>. The local stand-in for YR's per-ISIN HTTP endpoint; the
/// <see cref="NavChangeDetector"/> consuming it is identical in both
/// environments.
/// </summary>
/// <remarks>
/// Uses the copied read-only ORM model (<see cref="YieldRaccoonReadDbContext"/>)
/// mapping YR's <c>FundProfiles</c> + <c>FundHistoryRecords</c> tables. The
/// "latest NAV per fund" shape mirrors the canonical query in YR's
/// <c>FUND-DATABASE-AGENT-GUIDE.md</c>. Library code: awaits with
/// <c>ConfigureAwait(false)</c> and honours cancellation at IO boundaries.
/// </remarks>
public sealed class YieldRaccoonSqliteNavProvider : ILatestNavProvider
{
    private readonly ILogger _logger;
    private readonly string _dbPath;

    public YieldRaccoonSqliteNavProvider(ILogger logger, NavSyncOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _dbPath = options.YieldRaccoonDbPath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FundNavInfo>> GetLatestNavDatesAsync(CancellationToken ct = default)
    {
        // No path configured yet (Settings not filled in) → no funds, so
        // detection raises nothing rather than throwing on a missing file.
        if (string.IsNullOrWhiteSpace(_dbPath))
        {
            _logger.Trace("YR NAV read skipped — no database path configured");
            return Array.Empty<FundNavInfo>();
        }

        // Read-only EF query over YR's SQLite file — a heavy IO operation;
        // bracket it with start/stop traces so the cost is visible in the log.
        var stopwatch = Stopwatch.StartNew();
        _logger.Trace("YR NAV read starting — db={0}", _dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            // Don't hold a pooled handle on YR's file between detections.
            Pooling = false
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<YieldRaccoonReadDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using var db = new YieldRaccoonReadDbContext(options);

        // Latest NavDate per fund + its company. NavDate is ISO-8601 text, so
        // MAX orders chronologically (translated to a correlated subquery).
        var rows = await db.FundProfiles
            .AsNoTracking()
            .Where(fp => fp.HistoryRecords.Any(h => h.NavDate != null))
            .Select(fp => new
            {
                fp.Isin,
                fp.Name,
                fp.CompanyName,
                NavDate = fp.HistoryRecords.Max(h => h.NavDate)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _logger.Trace("YR NAV query returned {0} profile row(s) in {1} ms", rows.Count, stopwatch.ElapsedMilliseconds);

        var results = new List<FundNavInfo>(rows.Count);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.NavDate))
            {
                continue;
            }

            var navDate = DateTimeOffset.Parse(
                row.NavDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

            results.Add(new FundNavInfo(
                new Isin(row.Isin), navDate, row.CompanyName ?? string.Empty, row.Name ?? string.Empty));
        }

        stopwatch.Stop();
        _logger.Trace("YR NAV read done — {0} fund(s) mapped, {1} ms total", results.Count, stopwatch.ElapsedMilliseconds);

        return results;
    }
}