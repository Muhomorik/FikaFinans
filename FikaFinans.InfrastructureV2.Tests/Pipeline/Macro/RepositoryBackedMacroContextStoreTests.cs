using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Pipeline.Macro;
using FikaFinans.Infrastructure.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Macro;

[TestFixture]
[TestOf(typeof(RepositoryBackedMacroContextStore))]
public sealed class RepositoryBackedMacroContextStoreTests
{
    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;

    [SetUp]
    public async Task SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite(_connection)
            .Options;
        IDbContextFactory<BankDbContext> factory = new BankDbContextFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        _fixture.Inject<IMacroContextRepository>(new SqliteMacroContextRepository(factory));
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private RepositoryBackedMacroContextStore CreateSut()
        => _fixture.Create<RepositoryBackedMacroContextStore>();

    private static MacroContext Context(
        string week, string weeklySummaryRunId, string generatedAt = "2026-10-11T10:00:00.0000000+00:00") => new()
    {
        GeneratedAt = generatedAt,
        IsoWeek = week,
        ConfigVersion = "1.0.0",
        SourceRunIds = new SourceRunIds
        {
            WeeklySummaryRunId = weeklySummaryRunId,
            SubstitutionChainRunId = $"{weeklySummaryRunId}-chain",
            RotationTargetsRunId = $"{weeklySummaryRunId}-targets",
        },
        MacroRegime = MacroRegime.Mixed,
        RegimeConfidence = 0.5m,
        NetMoodInput = MarketSentiment.Mixed,
        Catalysts = [],
        RotationThemes = [],
    };

    #region GetAsync

    [Test]
    public async Task GetAsync_Saved_RoundTripsTheContext()
    {
        // Arrange
        var sut = CreateSut();
        await sut.SaveAsync(Context("2026-W41", "run-41"));

        // Act
        var result = await sut.GetAsync("run-41");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.IsoWeek, Is.EqualTo("2026-W41"));
            Assert.That(result.SourceRunIds.SubstitutionChainRunId.Value, Is.EqualTo("run-41-chain"));
            Assert.That(result.SourceRunIds.RotationTargetsRunId.Value, Is.EqualTo("run-41-targets"));
            Assert.That(result.MacroRegime, Is.EqualTo(MacroRegime.Mixed));
        });
    }

    [Test]
    public async Task GetAsync_RepublishedWeek_IsAMiss()
    {
        // Arrange — a corrected republish carries a new weekly summary runId.
        var sut = CreateSut();
        await sut.SaveAsync(Context("2026-W41", "run-41"));

        // Act
        var result = await sut.GetAsync("run-41-corrected");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region GetNewestAsync

    [Test]
    public async Task GetNewestAsync_NothingStored_ReturnsNull()
    {
        // Act
        var result = await CreateSut().GetNewestAsync(IsoWeek.From("2026-W41"));

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetNewestAsync_EarlierWeeksStored_ReturnsTheLatestOfThem()
    {
        // Arrange
        var sut = CreateSut();
        await sut.SaveAsync(Context("2026-W39", "run-39"));
        await sut.SaveAsync(Context("2026-W40", "run-40"));

        // Act
        var result = await sut.GetNewestAsync(IsoWeek.From("2026-W41"));

        // Assert
        Assert.That(result!.IsoWeek, Is.EqualTo("2026-W40"));
    }

    [Test]
    public async Task GetNewestAsync_SameWeekStored_IncludesIt()
    {
        // Arrange
        var sut = CreateSut();
        await sut.SaveAsync(Context("2026-W40", "run-40"));
        await sut.SaveAsync(Context("2026-W41", "run-41"));

        // Act
        var result = await sut.GetNewestAsync(IsoWeek.From("2026-W41"));

        // Assert
        Assert.That(result!.IsoWeek, Is.EqualTo("2026-W41"));
    }

    [Test]
    public async Task GetNewestAsync_OnlyLaterWeeksStored_ReturnsNull()
    {
        // Arrange — a later week's context is never used for an earlier fund.
        var sut = CreateSut();
        await sut.SaveAsync(Context("2026-W42", "run-42"));

        // Act
        var result = await sut.GetNewestAsync(IsoWeek.From("2026-W41"));

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetNewestAsync_TwoContextsForTheWeek_ReturnsTheNewerOne()
    {
        // Arrange — a republished week leaves two rows; the newer one wins.
        var sut = CreateSut();
        await sut.SaveAsync(Context("2026-W41", "run-41", "2026-10-08T10:00:00.0000000+00:00"));
        await sut.SaveAsync(Context("2026-W41", "run-41-corrected", "2026-10-10T10:00:00.0000000+00:00"));

        // Act
        var result = await sut.GetNewestAsync(IsoWeek.From("2026-W41"));

        // Assert
        Assert.That(result!.SourceRunIds.WeeklySummaryRunId.Value, Is.EqualTo("run-41-corrected"));
    }

    [Test]
    public async Task GetNewestAsync_AcrossYearBoundary_OrdersByWeek()
    {
        // Arrange
        var sut = CreateSut();
        await sut.SaveAsync(Context("2025-W52", "run-2025-52"));
        await sut.SaveAsync(Context("2026-W01", "run-2026-01"));

        // Act
        var result = await sut.GetNewestAsync(IsoWeek.From("2026-W02"));

        // Assert
        Assert.That(result!.IsoWeek, Is.EqualTo("2026-W01"));
    }

    #endregion
}
