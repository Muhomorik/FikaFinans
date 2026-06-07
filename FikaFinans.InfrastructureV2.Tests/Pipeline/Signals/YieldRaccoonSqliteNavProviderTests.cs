using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Infrastructure.Pipeline.Signals;
using FikaFinans.Infrastructure.YieldRaccoon;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Signals;

/// <summary>
/// Integration test for the read-only YieldRacoon NAV provider. Seeds a temp
/// SQLite file through the copied read-model (<see cref="YieldRaccoonReadDbContext"/>)
/// so the schema matches YR's real <c>FundProfiles</c> + <c>FundHistoryRecords</c>
/// tables, then verifies latest-date-per-ISIN + company mapping via EF.
/// </summary>
[TestFixture]
[TestOf(typeof(YieldRaccoonSqliteNavProvider))]
public sealed class YieldRaccoonSqliteNavProviderTests
{
    private string _dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"yr-nav-{Guid.NewGuid():N}.db");

        var connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<YieldRaccoonReadDbContext>().UseSqlite(connectionString).Options;

        using var db = new YieldRaccoonReadDbContext(options);
        db.Database.EnsureCreated();
        db.FundProfiles.AddRange(
            new FundProfile
            {
                Isin = "LU0001",
                CompanyName = "Acme",
                HistoryRecords =
                {
                    new FundHistoryRecord { FundId = "LU0001", NavDate = "2026-06-01" },
                    new FundHistoryRecord { FundId = "LU0001", NavDate = "2026-06-05" },
                },
            },
            new FundProfile
            {
                Isin = "LU0002",
                CompanyName = "Globex",
                HistoryRecords =
                {
                    new FundHistoryRecord { FundId = "LU0002", NavDate = "2026-06-03" },
                },
            });
        db.SaveChanges();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Test]
    public async Task GetLatestNavDatesAsync_ReturnsLatestNavDatePerFundWithCompany()
    {
        var sut = new YieldRaccoonSqliteNavProvider(new NavSyncOptions { YieldRaccoonDbPath = _dbPath });

        var infos = (await sut.GetLatestNavDatesAsync()).OrderBy(i => i.Isin.Value).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(infos, Has.Count.EqualTo(2));
            Assert.That(infos[0].Isin.Value, Is.EqualTo("LU0001"));
            Assert.That(infos[0].CompanyName, Is.EqualTo("Acme"));
            Assert.That(infos[0].NavDate, Is.EqualTo(new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero)),
                "latest of LU0001's two snapshots");
            Assert.That(infos[1].Isin.Value, Is.EqualTo("LU0002"));
            Assert.That(infos[1].CompanyName, Is.EqualTo("Globex"));
            Assert.That(infos[1].NavDate, Is.EqualTo(new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task GetLatestNavDatesAsync_EmptyPath_ReturnsEmpty()
    {
        // Unconfigured path is a no-op rather than a throw — local dev before
        // the YR DB path is set in Settings.
        var sut = new YieldRaccoonSqliteNavProvider(new NavSyncOptions { YieldRaccoonDbPath = string.Empty });

        var infos = await sut.GetLatestNavDatesAsync();

        Assert.That(infos, Is.Empty);
    }
}
