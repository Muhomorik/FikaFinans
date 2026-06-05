using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Storage.Sqlite;

[TestFixture]
[TestOf(typeof(SqliteIsinProgressRepository))]
public sealed class SqliteIsinProgressRepositoryTests
{
    private const string Partition = "isin-progress";

    private SqliteConnection _connection = null!;
    private IDbContextFactory<BankDbContext> _factory = null!;
    private SqliteIsinProgressRepository _sut = null!;

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

        _factory = new BankDbContextFactory(options);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        _sut = new SqliteIsinProgressRepository(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
    }

    [Test]
    public async Task GetAsync_MissingRow_ReturnsNull()
    {
        var result = await _sut.GetAsync(Partition, "LU0000000001");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpsertAsync_NewRow_PersistsAllColumns()
    {
        var entity = new IsinProgressEntity
        {
            PartitionKey = Partition,
            RowKey = "LU0000000001",
            Isin = "LU0000000001",
            State = IsinProgressState.Processing,
            CurrentStep = 4,
            Step01Json = "{\"step\":1}",
            Step04Json = "{\"step\":4}",
            ProcessingStartedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            LastError = "none",
            AttemptCount = 1,
        };

        await _sut.UpsertAsync(entity);

        var roundTripped = await _sut.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Isin, Is.EqualTo("LU0000000001"));
            Assert.That(roundTripped.State, Is.EqualTo(IsinProgressState.Processing));
            Assert.That(roundTripped.CurrentStep, Is.EqualTo(4));
            Assert.That(roundTripped.Step01Json, Is.EqualTo("{\"step\":1}"));
            Assert.That(roundTripped.Step04Json, Is.EqualTo("{\"step\":4}"));
            Assert.That(roundTripped.Step02Json, Is.Null);
            Assert.That(roundTripped.ProcessingStartedAt,
                Is.EqualTo(new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero)));
            Assert.That(roundTripped.LastError, Is.EqualTo("none"));
            Assert.That(roundTripped.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UpsertAsync_ExistingRow_OverwritesAllColumns()
    {
        var first = new IsinProgressEntity
        {
            PartitionKey = Partition,
            RowKey = "LU0000000001",
            Isin = "LU0000000001",
            State = IsinProgressState.Processing,
            CurrentStep = 2,
            Step02Json = "old",
        };
        await _sut.UpsertAsync(first);

        var second = new IsinProgressEntity
        {
            PartitionKey = Partition,
            RowKey = "LU0000000001",
            Isin = "LU0000000001",
            State = IsinProgressState.Free,
            CurrentStep = 9,
            RunId = new PipelineRunId("run-2"),
            Step02Json = null,
            Step09Json = "new",
            LatestProcessedNavDate = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        };
        await _sut.UpsertAsync(second);

        var roundTripped = await _sut.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(roundTripped.CurrentStep, Is.EqualTo(9));
            Assert.That(roundTripped.RunId, Is.EqualTo(new PipelineRunId("run-2")));
            Assert.That(roundTripped.Step02Json, Is.Null);
            Assert.That(roundTripped.Step09Json, Is.EqualTo("new"));
            Assert.That(roundTripped.LatestProcessedNavDate,
                Is.EqualTo(new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task QueryPartitionAsync_ReturnsAllRowsInPartition()
    {
        await _sut.UpsertAsync(MakeEntity("LU0000000001"));
        await _sut.UpsertAsync(MakeEntity("LU0000000002"));
        await _sut.UpsertAsync(MakeEntity("LU0000000003"));

        var rows = await _sut.QueryPartitionAsync(Partition);

        Assert.That(rows.Select(r => r.Isin),
            Is.EquivalentTo(new[] { "LU0000000001", "LU0000000002", "LU0000000003" }));
    }

    [Test]
    public async Task QueryPartitionAsync_EmptyPartition_ReturnsEmptyList()
    {
        var rows = await _sut.QueryPartitionAsync(Partition);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task UpsertBatchAsync_AllRowsLandInTargetPartition()
    {
        var batch = new[]
        {
            MakeEntity("LU0000000001"),
            MakeEntity("LU0000000002"),
        };

        await _sut.UpsertBatchAsync(Partition, batch);

        var rows = await _sut.QueryPartitionAsync(Partition);
        Assert.That(rows.Select(r => r.RowKey),
            Is.EquivalentTo(new[] { "LU0000000001", "LU0000000002" }));
    }

    [Test]
    public void UpsertBatchAsync_CrossPartitionRow_Throws()
    {
        var mixed = new[]
        {
            MakeEntity("LU0000000001"),
            new IsinProgressEntity
            {
                PartitionKey = "wrong",
                RowKey = "LU0000000002",
                Isin = "LU0000000002",
                State = IsinProgressState.Free,
            },
        };

        Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpsertBatchAsync(Partition, mixed));
    }

    [Test]
    public async Task DeleteAsync_RemovesTargetRow()
    {
        await _sut.UpsertAsync(MakeEntity("LU0000000001"));
        await _sut.UpsertAsync(MakeEntity("LU0000000002"));

        await _sut.DeleteAsync(Partition, "LU0000000001");

        var rows = await _sut.QueryPartitionAsync(Partition);
        Assert.That(rows.Select(r => r.Isin),
            Is.EqualTo(new[] { "LU0000000002" }));
    }

    [Test]
    public void UpsertAsync_NullEntity_Throws()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.UpsertAsync(null!));
    }

    private static IsinProgressEntity MakeEntity(
        string isin,
        IsinProgressState state = IsinProgressState.Free,
        int currentStep = 0) => new()
    {
        PartitionKey = Partition,
        RowKey = isin,
        Isin = isin,
        State = state,
        CurrentStep = currentStep,
    };
}
