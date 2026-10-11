using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Storage.Sqlite;

[TestFixture]
[TestOf(typeof(SqliteMacroContextRepository))]
public sealed class SqliteMacroContextRepositoryTests
{
    private const string Partition = "macro-contexts";

    private SqliteConnection _connection = null!;
    private SqliteMacroContextRepository _sut = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Shared-cache :memory: DB lives only as long as _connection stays open;
        // each test owns its own throwaway database.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite(_connection)
            .Options;
        IDbContextFactory<BankDbContext> factory = new BankDbContextFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        _sut = new SqliteMacroContextRepository(factory);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private static MacroContextEntity Entity(string runId, string week, string json = "{}") => new()
    {
        PartitionKey = Partition,
        RowKey = runId,
        IsoWeek = week,
        GeneratedAt = "2026-10-11T10:00:00.0000000+00:00",
        ContextJson = json,
    };

    [Test]
    public async Task GetAsync_MissingRow_ReturnsNull()
    {
        // Act
        var result = await _sut.GetAsync(Partition, "run-1");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpsertAsync_NewRow_PersistsAllColumns()
    {
        // Arrange
        var entity = Entity("run-1", "2026-W41", """{ "iso_week": "2026-W41" }""");

        // Act
        await _sut.UpsertAsync(entity);
        var result = await _sut.GetAsync(Partition, "run-1");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.IsoWeek, Is.EqualTo("2026-W41"));
            Assert.That(result.GeneratedAt, Is.EqualTo(entity.GeneratedAt));
            Assert.That(result.ContextJson, Is.EqualTo(entity.ContextJson));
        });
    }

    [Test]
    public async Task UpsertAsync_ExistingRow_ReplacesColumns()
    {
        // Arrange
        await _sut.UpsertAsync(Entity("run-1", "2026-W41", "{ \"v\": 1 }"));

        // Act
        await _sut.UpsertAsync(Entity("run-1", "2026-W41", "{ \"v\": 2 }"));
        var all = await _sut.QueryPartitionAsync(Partition);

        // Assert — last write wins, still one row.
        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].ContextJson, Is.EqualTo("{ \"v\": 2 }"));
    }

    [Test]
    public async Task QueryPartitionAsync_SeveralRows_ReturnsThemAll()
    {
        // Arrange
        await _sut.UpsertAsync(Entity("run-1", "2026-W40"));
        await _sut.UpsertAsync(Entity("run-2", "2026-W41"));

        // Act
        var all = await _sut.QueryPartitionAsync(Partition);

        // Assert
        Assert.That(all.Select(e => e.RowKey), Is.EquivalentTo(new[] { "run-1", "run-2" }));
    }
}
