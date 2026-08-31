using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Fetch;
using FikaFinans.Infrastructure.YieldRaccoon;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Fetch;

/// <summary>
/// Integration test for the read-only YieldRacoon metadata provider. Seeds a
/// temp SQLite file through the copied read-model
/// (<see cref="YieldRaccoonReadDbContext"/>) so the schema matches YR's real
/// <c>FundProfiles</c> table, then verifies the per-ISIN mapping onto
/// <see cref="FundMetadata"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(YieldRaccoonSqliteMetadataProvider))]
public sealed class YieldRaccoonSqliteMetadataProviderTests
{
    private const string KnownIsin = "LU0106252389";

    private IFixture _fixture = null!;
    private string _dbPath = null!;
    private YieldRaccoonSqliteMetadataProvider _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"yr-meta-{Guid.NewGuid():N}.db");
        SeedDatabase(_dbPath);

        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject(new NavSyncOptions { YieldRaccoonDbPath = _dbPath });
        _sut = _fixture.Create<YieldRaccoonSqliteMetadataProvider>();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static void SeedDatabase(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<YieldRaccoonReadDbContext>().UseSqlite(connectionString).Options;

        using var db = new YieldRaccoonReadDbContext(options);
        db.Database.EnsureCreated();
        db.FundProfiles.AddRange(
            new FundProfile
            {
                Isin = KnownIsin,
                Name = "Schroder ISF Em Mkts A Acc USD",
                // Column preserves original case; the filename token is lower-cased.
                CompanyName = "Schroder",
                CurrencyCode = "USD",
                Category = "Tillväxtmarknader",
                FundType = "EQUITY_FUND",
                IsIndexFund = false,
                ManagedType = "ACTIVE",
                TotalFee = 2.17m,
                ManagementFee = 1.5m,
                Risk = 4,
                Rating = 3,
                SharpeRatio = 0.68m,
                StandardDeviation = 14.28m,
                RecommendedHoldingPeriod = "FIVE_YEAR",
                Capital = 70121004753.02m,
                NumberOfOwners = 410,
                Buyable = true,
            },
            new FundProfile
            {
                Isin = "LU0000000002",
                Name = "Globex Asian Growth",
                CompanyName = "Globex",
            });
        db.SaveChanges();
    }

    #region Happy path

    [Test]
    [TestOf(nameof(YieldRaccoonSqliteMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_KnownIsin_MapsEveryColumn()
    {
        // Arrange
        var isin = new Isin(KnownIsin);

        // Act
        var result = await _sut.GetMetadataAsync(isin, Company.From("Schroder"), IsoWeek.From("2026-W18"));

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Isin.Value, Is.EqualTo(KnownIsin));
            Assert.That(result.Name, Is.EqualTo("Schroder ISF Em Mkts A Acc USD"));
            Assert.That(result.CompanyName, Is.EqualTo("Schroder"));
            Assert.That(result.CurrencyCode, Is.EqualTo("USD"));
            Assert.That(result.Category, Is.EqualTo("Tillväxtmarknader"));
            Assert.That(result.FundType, Is.EqualTo("EQUITY_FUND"));
            Assert.That(result.IsIndexFund, Is.False);
            Assert.That(result.ManagedType, Is.EqualTo("ACTIVE"));
            Assert.That(result.TotalFee, Is.EqualTo(2.17m));
            Assert.That(result.ManagementFee, Is.EqualTo(1.5m));
            Assert.That(result.Risk, Is.EqualTo(4));
            Assert.That(result.Rating, Is.EqualTo(3));
            Assert.That(result.SharpeRatioStatic, Is.EqualTo(0.68m));
            Assert.That(result.StandardDeviationStatic, Is.EqualTo(14.28m));
            Assert.That(result.RecommendedHoldingPeriod, Is.EqualTo("FIVE_YEAR"));
            Assert.That(result.Capital, Is.EqualTo(70121004753.02m));
            Assert.That(result.NumberOfOwners, Is.EqualTo(410));
        });
    }

    [Test]
    [TestOf(nameof(YieldRaccoonSqliteMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_CompanyCasingDiffers_ReturnsMetadata()
    {
        // The filename token is lower-cased while the column preserves case, so
        // the company match must be case-insensitive.
        // Arrange
        var isin = new Isin(KnownIsin);

        // Act
        var result = await _sut.GetMetadataAsync(isin, Company.From("schroder"), IsoWeek.From("2026-W18"));

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.CompanyName, Is.EqualTo("Schroder"));
    }

    #endregion

    #region Nothing to return

    [Test]
    [TestOf(nameof(YieldRaccoonSqliteMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_UnknownIsin_ReturnsNull()
    {
        // Arrange
        var isin = new Isin("LU9999999999");

        // Act
        var result = await _sut.GetMetadataAsync(isin, Company.From("Schroder"), IsoWeek.From("2026-W18"));

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [TestOf(nameof(YieldRaccoonSqliteMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_FundBelongsToAnotherCompany_ReturnsNull()
    {
        // Arrange
        var isin = new Isin(KnownIsin);

        // Act
        var result = await _sut.GetMetadataAsync(isin, Company.From("Globex"), IsoWeek.From("2026-W18"));

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [TestOf(nameof(YieldRaccoonSqliteMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_EmptyDbPath_ReturnsNull()
    {
        // Unconfigured path is a no-op rather than a throw — local dev before
        // the YR DB path is set in Settings. Matches YieldRaccoonSqliteNavProvider.
        // Arrange
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        fixture.Inject(new NavSyncOptions { YieldRaccoonDbPath = string.Empty });
        var sut = fixture.Create<YieldRaccoonSqliteMetadataProvider>();

        // Act
        var result = await sut.GetMetadataAsync(new Isin(KnownIsin), Company.From("Schroder"), IsoWeek.From("2026-W18"));

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Nullable columns

    [Test]
    [TestOf(nameof(YieldRaccoonSqliteMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_NullableColumnsEmpty_MapsToEmptyStringsAndZeroes()
    {
        // Every column except isin, name and number_of_owners can be empty in
        // the producer's data, but FundMetadata declares most of them required.
        // Arrange
        var isin = new Isin("LU0000000002");

        // Act
        var result = await _sut.GetMetadataAsync(isin, Company.From("Globex"), IsoWeek.From("2026-W18"));

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.CurrencyCode, Is.Empty);
            Assert.That(result.Category, Is.Empty);
            Assert.That(result.FundType, Is.Empty);
            Assert.That(result.ManagedType, Is.Empty);
            Assert.That(result.RecommendedHoldingPeriod, Is.Empty);
            Assert.That(result.IsIndexFund, Is.Null);
            Assert.That(result.Risk, Is.Null);
            Assert.That(result.Rating, Is.Null);
            Assert.That(result.SharpeRatioStatic, Is.Null);
            Assert.That(result.StandardDeviationStatic, Is.Null);
            Assert.That(result.TotalFee, Is.Zero);
            Assert.That(result.ManagementFee, Is.Zero);
            Assert.That(result.Capital, Is.Zero);
            Assert.That(result.NumberOfOwners, Is.Zero);
        });
    }

    #endregion
}
