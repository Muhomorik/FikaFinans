using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Pipeline.Progress;
using FikaFinans.Infrastructure.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Progress;

[TestFixture]
[TestOf(typeof(RepositoryBackedIsinProgressStore))]
public sealed class RepositoryBackedIsinProgressStoreTests
{
    private const string Partition = "isin-progress";
    private const string FundIsin = "LU0000000001";

    private static readonly DateTimeOffset Date1 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Date2 = new(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartedAt = new(2026, 6, 5, 18, 30, 0, TimeSpan.Zero);
    private static readonly PipelineRunId RunId = new("run-1");

    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;
    private SqliteIsinProgressRepository _repo = null!;

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

        _repo = new SqliteIsinProgressRepository(factory);
        _fixture.Inject<IIsinProgressRepository>(_repo);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    // ── CompleteStep01Async ───────────────────────────────────────────

    [Test]
    public async Task CompleteStep01Async_InFlightRow_FreesRowAndAdvancesAnchorToNavDate()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep01Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date2));
        });
    }

    [Test]
    public async Task CompleteStep01Async_PreviousFailure_ClearsLastErrorAndAttemptCount()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 2);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep01Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.LastError, Is.Null);
            Assert.That(row.AttemptCount, Is.Zero);
        });
    }

    [Test]
    public async Task CompleteStep01Async_NoNavDate_KeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: null, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep01Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.That(row!.LatestProcessedNavDate, Is.EqualTo(Date1));
    }

    [Test]
    public async Task CompleteStep01Async_InFlightRow_KeepsRunIdAndStepColumns()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep01Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.RunId, Is.EqualTo(new PipelineRunId("run-1")));
            Assert.That(row.NavDate, Is.EqualTo(Date2));
            Assert.That(row.CurrentStep, Is.EqualTo(1));
            Assert.That(row.Step01Json, Is.EqualTo("step1"));
        });
    }

    [Test]
    public async Task CompleteStep01Async_InFlightRow_RowCanBeStartedAgain()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep01Async(new Isin(FundIsin));
        var started = await sut.StartStep01Async(new Isin(FundIsin), Date2.AddDays(1));

        // Assert
        Assert.That(started, Is.Not.Null);
    }

    [Test]
    public async Task CompleteStep01Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var completed = await sut.CompleteStep01Async(new Isin(FundIsin));

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    // ── FailStep01Async ───────────────────────────────────────────────

    [Test]
    public async Task FailStep01Async_InFlightRow_FreesRowAndKeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, attemptCount: 1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep01Async(new Isin(FundIsin), "Step 1 exploded");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1),
                "a failed run must not advance the anchor, so the next signal retries the fund");
            Assert.That(row.LastError, Is.EqualTo("Step 1 exploded"));
            Assert.That(row.AttemptCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task FailStep01Async_InFlightRow_KeepsRunIdAndStepColumns()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep01Async(new Isin(FundIsin), "Step 1 exploded");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.RunId, Is.EqualTo(new PipelineRunId("run-1")));
            Assert.That(row.NavDate, Is.EqualTo(Date2));
            Assert.That(row.CurrentStep, Is.EqualTo(1));
            Assert.That(row.Step01Json, Is.EqualTo("step1"));
        });
    }

    [Test]
    public async Task FailStep01Async_InFlightRow_RowCanBeStartedAgain()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep01Async(new Isin(FundIsin), "Step 1 exploded");
        var started = await sut.StartStep01Async(new Isin(FundIsin), Date2);

        // Assert
        Assert.That(started, Is.Not.Null);
    }

    [Test]
    public async Task FailStep01Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var failed = await sut.FailStep01Async(new Isin(FundIsin), "Step 1 exploded");

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    [Test]
    public void FailStep01Async_EmptyError_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.FailStep01Async(new Isin(FundIsin), string.Empty));
    }

    // ── StartStep02Async ──────────────────────────────────────────────

    [Test]
    public async Task StartStep02Async_InFlightSameRun_MovesCurrentStepTo2()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep02Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Not.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StartStep02Async_InFlightSameRun_KeepsEverythingElse()
    {
        // Arrange — unlike step 1's start, this one must not clear the row: step 1's
        // output is step 2's input, and the start time belongs to the run, not the step.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.StartStep02Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Processing));
            Assert.That(row.RunId, Is.EqualTo(RunId));
            Assert.That(row.NavDate, Is.EqualTo(Date2));
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1));
            Assert.That(row.ProcessingStartedAt, Is.EqualTo(StartedAt));
            Assert.That(row.LastError, Is.EqualTo("old failure"));
            Assert.That(row.AttemptCount, Is.EqualTo(1));
            Assert.That(row.Step01Json, Is.EqualTo("step1"));
        });
    }

    [Test]
    public async Task StartStep02Async_DifferentRunId_ReturnsNullAndLeavesRowAlone()
    {
        // Arrange — a late signal: a newer run has started step 1 on the fund since.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep02Async(new Isin(FundIsin), new PipelineRunId("older-run"));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartStep02Async_RowFree_ReturnsNullAndLeavesRowAlone()
    {
        // Arrange — the run already ended, so a signal from it is late too.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.FailStep01Async(new Isin(FundIsin), "Step 1 exploded");

        // Act
        var started = await sut.StartStep02Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartStep02Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep02Async(new Isin(FundIsin), RunId);

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    [Test]
    public void StartStep02Async_EmptyRunId_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.StartStep02Async(new Isin(FundIsin), new PipelineRunId(string.Empty)));
    }

    // ── CompleteStep02Async / FailStep02Async ─────────────────────────

    [Test]
    public async Task CompleteStep02Async_InFlightRow_FreesRowAndAdvancesAnchorToNavDate()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep02Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date2));
            Assert.That(row.LastError, Is.Null);
        });
    }

    [Test]
    public async Task FailStep02Async_InFlightRow_FreesRowAndKeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep02Async(new Isin(FundIsin), "Step 2 exploded");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1));
            Assert.That(row.LastError, Is.EqualTo("Step 2 exploded"));
            Assert.That(row.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FailStep02Async_EmptyError_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.FailStep02Async(new Isin(FundIsin), string.Empty));
    }

    private Task SeedInFlightAsync(
        DateTimeOffset? navDate, DateTimeOffset? anchor, string? lastError = null, int attemptCount = 0)
        => _repo.UpsertAsync(new IsinProgressEntity
        {
            PartitionKey = Partition,
            RowKey = FundIsin,
            Isin = FundIsin,
            State = IsinProgressState.Processing,
            RunId = RunId,
            NavDate = navDate,
            CurrentStep = 1,
            LatestProcessedNavDate = anchor,
            ProcessingStartedAt = StartedAt,
            LastError = lastError,
            AttemptCount = attemptCount,
            Step01Json = "step1",
        });
}
