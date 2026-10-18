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

    // ── CompleteStep05Async ───────────────────────────────────────────

    [Test]
    public async Task CompleteStep05Async_InFlightRow_FreesRowAndAdvancesAnchorToNavDate()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep05Async(new Isin(FundIsin));

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
    public async Task CompleteStep05Async_PreviousFailure_ClearsLastErrorAndAttemptCount()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 2);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep05Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.LastError, Is.Null);
            Assert.That(row.AttemptCount, Is.Zero);
        });
    }

    [Test]
    public async Task CompleteStep05Async_NoNavDate_KeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: null, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep05Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.That(row!.LatestProcessedNavDate, Is.EqualTo(Date1));
    }

    [Test]
    public async Task CompleteStep05Async_InFlightRow_KeepsRunIdAndStepColumns()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep05Async(new Isin(FundIsin));

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
    public async Task CompleteStep05Async_InFlightRow_RowCanBeStartedAgain()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep05Async(new Isin(FundIsin));
        var started = await sut.StartStep01Async(new Isin(FundIsin), Date2.AddDays(1));

        // Assert
        Assert.That(started, Is.Not.Null);
    }

    [Test]
    public async Task CompleteStep05Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var completed = await sut.CompleteStep05Async(new Isin(FundIsin));

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

    // ── FailStep02Async ───────────────────────────────────────────────

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

    // ── ListOrphanedStep01Async / ListOrphanedStep02Async ─────────────

    private static readonly DateTimeOffset SessionStart = new(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ListOrphanedStep01Async_StartedBeforeSession_ReturnsRow()
    {
        // Arrange
        await SeedRowAsync("LU0000000011", IsinProgressState.Processing, step: 1, startedAt: SessionStart.AddHours(-1));
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep01Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EqualTo(new[] { "LU0000000011" }));
    }

    [Test]
    public async Task ListOrphanedStep01Async_StartedDuringSession_LeavesItOut()
    {
        // Arrange — may genuinely be running right now.
        await SeedRowAsync("LU0000000012", IsinProgressState.Processing, step: 1, startedAt: SessionStart.AddMinutes(5));
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep01Async(SessionStart);

        // Assert
        Assert.That(orphaned, Is.Empty);
    }

    [Test]
    public async Task ListOrphanedStep01Async_AtStep2_LeavesItOut()
    {
        // Arrange — step 2's tab recovers that one.
        await SeedRowAsync("LU0000000013", IsinProgressState.Processing, step: 2, startedAt: SessionStart.AddHours(-1));
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep01Async(SessionStart);

        // Assert
        Assert.That(orphaned, Is.Empty);
    }

    [Test]
    public async Task ListOrphanedStep01Async_Free_LeavesItOut()
    {
        // Arrange
        await SeedRowAsync("LU0000000014", IsinProgressState.Free, step: 1, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep01Async(SessionStart);

        // Assert
        Assert.That(orphaned, Is.Empty);
    }

    [Test]
    public async Task ListOrphanedStep01Async_ProcessingWithoutStartTime_ReturnsRow()
    {
        // Arrange — every path that marks a row processing stamps the time, so a missing
        // one cannot come from this session; leaving it out would lock the fund for good.
        await SeedRowAsync("LU0000000015", IsinProgressState.Processing, step: 1, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep01Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EqualTo(new[] { "LU0000000015" }));
    }

    [Test]
    public async Task ListOrphanedStep01Async_NoRows_ReturnsEmpty()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep01Async(SessionStart);

        // Assert
        Assert.That(orphaned, Is.Empty);
    }

    [Test]
    public async Task ListOrphanedStep02Async_MixedRows_ReturnsOnlyStep2RunsStartedBeforeSession()
    {
        // Arrange
        await SeedRowAsync("LU0000000021", IsinProgressState.Processing, step: 2, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000022", IsinProgressState.Processing, step: 1, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000023", IsinProgressState.Processing, step: 2, startedAt: SessionStart.AddMinutes(5));
        await SeedRowAsync("LU0000000024", IsinProgressState.Free, step: 2, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep02Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EqualTo(new[] { "LU0000000021" }));
    }

    // ── StartStep03Async ──────────────────────────────────────────────

    [Test]
    public async Task StartStep03Async_InFlightSameRunAtStep2_MovesCurrentStepTo3()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.StartStep02Async(new Isin(FundIsin), RunId);

        // Act
        var started = await sut.StartStep03Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Not.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task StartStep03Async_InFlightSameRun_KeepsEverythingElse()
    {
        // Arrange — step 2's output is step 3's input, so nothing is cleared.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.StartStep03Async(new Isin(FundIsin), RunId);

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
    public async Task StartStep03Async_DifferentRunId_ReturnsNullAndLeavesRowAlone()
    {
        // Arrange — a late signal: a newer run has started step 1 on the fund since.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep03Async(new Isin(FundIsin), new PipelineRunId("older-run"));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartStep03Async_RowFree_ReturnsNull()
    {
        // Arrange — the run already ended, so a signal from it is late too.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.FailStep02Async(new Isin(FundIsin), "Step 2 exploded");

        // Act
        var started = await sut.StartStep03Async(new Isin(FundIsin), RunId);

        // Assert
        Assert.That(started, Is.Null);
    }

    [Test]
    public async Task StartStep03Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep03Async(new Isin(FundIsin), RunId);

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    [Test]
    public void StartStep03Async_EmptyRunId_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.StartStep03Async(new Isin(FundIsin), new PipelineRunId(string.Empty)));
    }

    // ── FailStep03Async ───────────────────────────────────────────────

    [Test]
    public async Task FailStep03Async_InFlightRow_FreesRowAndKeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep03Async(new Isin(FundIsin), "no analytics reports for 2026-W23");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1));
            Assert.That(row.LastError, Is.EqualTo("no analytics reports for 2026-W23"));
            Assert.That(row.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FailStep03Async_EmptyError_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.FailStep03Async(new Isin(FundIsin), string.Empty));
    }

    // ── ListOrphanedStep03Async ───────────────────────────────────────

    [Test]
    public async Task ListOrphanedStep03Async_MixedRows_ReturnsOnlyStep3RunsStartedBeforeSession()
    {
        // Arrange
        await SeedRowAsync("LU0000000031", IsinProgressState.Processing, step: 3, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000032", IsinProgressState.Processing, step: 2, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000033", IsinProgressState.Processing, step: 3, startedAt: SessionStart.AddMinutes(5));
        await SeedRowAsync("LU0000000034", IsinProgressState.Free, step: 3, startedAt: null);
        await SeedRowAsync("LU0000000035", IsinProgressState.Processing, step: 3, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep03Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EquivalentTo(new[] { "LU0000000031", "LU0000000035" }));
    }

    // ── StartStep04Async ──────────────────────────────────────────────

    [Test]
    public async Task StartStep04Async_InFlightSameRunAtStep3_MovesCurrentStepTo4()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.StartStep03Async(new Isin(FundIsin), RunId);

        // Act
        var started = await sut.StartStep04Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Not.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task StartStep04Async_InFlightSameRun_KeepsEverythingElse()
    {
        // Arrange — step 3's output is step 4's input, so nothing is cleared.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.StartStep04Async(new Isin(FundIsin), RunId);

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
    public async Task StartStep04Async_DifferentRunId_ReturnsNullAndLeavesRowAlone()
    {
        // Arrange — a late signal: a newer run has started step 1 on the fund since.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep04Async(new Isin(FundIsin), new PipelineRunId("older-run"));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartStep04Async_RowFree_ReturnsNull()
    {
        // Arrange — the run already ended, so a signal from it is late too.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.FailStep03Async(new Isin(FundIsin), "Step 3 exploded");

        // Act
        var started = await sut.StartStep04Async(new Isin(FundIsin), RunId);

        // Assert
        Assert.That(started, Is.Null);
    }

    [Test]
    public async Task StartStep04Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep04Async(new Isin(FundIsin), RunId);

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    [Test]
    public void StartStep04Async_EmptyRunId_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.StartStep04Async(new Isin(FundIsin), new PipelineRunId(string.Empty)));
    }

    // ── FailStep04Async ───────────────────────────────────────────────

    [Test]
    public async Task FailStep04Async_InFlightRow_FreesRowAndKeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep04Async(new Isin(FundIsin), "Step 3 stored no record");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1));
            Assert.That(row.LastError, Is.EqualTo("Step 3 stored no record"));
            Assert.That(row.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FailStep04Async_EmptyError_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.FailStep04Async(new Isin(FundIsin), string.Empty));
    }

    // ── ListOrphanedStep04Async ───────────────────────────────────────

    [Test]
    public async Task ListOrphanedStep04Async_MixedRows_ReturnsOnlyStep4RunsStartedBeforeSession()
    {
        // Arrange
        await SeedRowAsync("LU0000000041", IsinProgressState.Processing, step: 4, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000042", IsinProgressState.Processing, step: 3, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000043", IsinProgressState.Processing, step: 4, startedAt: SessionStart.AddMinutes(5));
        await SeedRowAsync("LU0000000044", IsinProgressState.Free, step: 4, startedAt: null);
        await SeedRowAsync("LU0000000045", IsinProgressState.Processing, step: 4, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep04Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EquivalentTo(new[] { "LU0000000041", "LU0000000045" }));
    }

    // ── StartStep05Async ──────────────────────────────────────────────

    [Test]
    public async Task StartStep05Async_InFlightSameRunAtStep4_MovesCurrentStepTo5()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.StartStep04Async(new Isin(FundIsin), RunId);

        // Act
        var started = await sut.StartStep05Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Not.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task StartStep05Async_InFlightSameRun_KeepsEverythingElse()
    {
        // Arrange — step 4's output is step 5's input, so nothing is cleared.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.StartStep05Async(new Isin(FundIsin), RunId);

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
    public async Task StartStep05Async_DifferentRunId_ReturnsNullAndLeavesRowAlone()
    {
        // Arrange — a late signal: a newer run has started step 1 on the fund since.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep05Async(new Isin(FundIsin), new PipelineRunId("older-run"));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartStep05Async_RowFree_ReturnsNull()
    {
        // Arrange — the run already ended, so a signal from it is late too.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.FailStep04Async(new Isin(FundIsin), "Step 4 exploded");

        // Act
        var started = await sut.StartStep05Async(new Isin(FundIsin), RunId);

        // Assert
        Assert.That(started, Is.Null);
    }

    [Test]
    public async Task StartStep05Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep05Async(new Isin(FundIsin), RunId);

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    [Test]
    public void StartStep05Async_EmptyRunId_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.StartStep05Async(new Isin(FundIsin), new PipelineRunId(string.Empty)));
    }

    // ── FailStep05Async ───────────────────────────────────────────────

    [Test]
    public async Task FailStep05Async_InFlightRow_FreesRowAndKeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep05Async(new Isin(FundIsin), "no macro context for 2026-W23 or earlier");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1));
            Assert.That(row.LastError, Is.EqualTo("no macro context for 2026-W23 or earlier"));
            Assert.That(row.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FailStep05Async_EmptyError_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.FailStep05Async(new Isin(FundIsin), string.Empty));
    }

    // ── ListOrphanedStep05Async ───────────────────────────────────────

    [Test]
    public async Task ListOrphanedStep05Async_MixedRows_ReturnsOnlyStep5RunsStartedBeforeSession()
    {
        // Arrange
        await SeedRowAsync("LU0000000051", IsinProgressState.Processing, step: 5, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000052", IsinProgressState.Processing, step: 4, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000053", IsinProgressState.Processing, step: 5, startedAt: SessionStart.AddMinutes(5));
        await SeedRowAsync("LU0000000054", IsinProgressState.Free, step: 5, startedAt: null);
        await SeedRowAsync("LU0000000055", IsinProgressState.Processing, step: 5, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep05Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EquivalentTo(new[] { "LU0000000051", "LU0000000055" }));
    }

    // ── StartStep06Async ──────────────────────────────────────────────

    [Test]
    public async Task StartStep06Async_InFlightSameRunAtStep5_MovesCurrentStepTo6()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.StartStep05Async(new Isin(FundIsin), RunId);

        // Act
        var started = await sut.StartStep06Async(new Isin(FundIsin), RunId);

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Not.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(6));
        });
    }

    [Test]
    public async Task StartStep06Async_InFlightSameRun_KeepsEverythingElse()
    {
        // Arrange — step 5's output is step 6's input, so nothing is cleared.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1, lastError: "old failure", attemptCount: 1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.StartStep06Async(new Isin(FundIsin), RunId);

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
    public async Task StartStep06Async_DifferentRunId_ReturnsNullAndLeavesRowAlone()
    {
        // Arrange — a late signal: a newer run has started step 1 on the fund since.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep06Async(new Isin(FundIsin), new PipelineRunId("older-run"));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(row!.CurrentStep, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartStep06Async_RowFree_ReturnsNull()
    {
        // Arrange — the run already ended, so a signal from it is late too.
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();
        await sut.FailStep05Async(new Isin(FundIsin), "Step 5 exploded");

        // Act
        var started = await sut.StartStep06Async(new Isin(FundIsin), RunId);

        // Assert
        Assert.That(started, Is.Null);
    }

    [Test]
    public async Task StartStep06Async_MissingRow_ReturnsNullAndWritesNothing()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var started = await sut.StartStep06Async(new Isin(FundIsin), RunId);

        // Assert
        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Null);
            Assert.That(rows, Is.Empty);
        });
    }

    [Test]
    public void StartStep06Async_EmptyRunId_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.StartStep06Async(new Isin(FundIsin), new PipelineRunId(string.Empty)));
    }

    // ── CompleteStep06Async ───────────────────────────────────────────
    // The rest of the complete cases move here from step 5 when step 5 gives up the tail.

    [Test]
    public async Task CompleteStep06Async_InFlightRow_FreesRowAndAdvancesAnchorToNavDate()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.CompleteStep06Async(new Isin(FundIsin));

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date2));
        });
    }

    // ── FailStep06Async ───────────────────────────────────────────────

    [Test]
    public async Task FailStep06Async_InFlightRow_FreesRowAndKeepsAnchor()
    {
        // Arrange
        await SeedInFlightAsync(navDate: Date2, anchor: Date1);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        await sut.FailStep06Async(new Isin(FundIsin), "no macro context for 2026-W23 or earlier");

        // Assert
        var row = await _repo.GetAsync(Partition, FundIsin);
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.ProcessingStartedAt, Is.Null);
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(Date1));
            Assert.That(row.LastError, Is.EqualTo("no macro context for 2026-W23 or earlier"));
            Assert.That(row.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FailStep06Async_EmptyError_ThrowsArgumentException()
    {
        // Arrange
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.FailStep06Async(new Isin(FundIsin), string.Empty));
    }

    // ── ListOrphanedStep06Async ───────────────────────────────────────

    [Test]
    public async Task ListOrphanedStep06Async_MixedRows_ReturnsOnlyStep6RunsStartedBeforeSession()
    {
        // Arrange
        await SeedRowAsync("LU0000000061", IsinProgressState.Processing, step: 6, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000062", IsinProgressState.Processing, step: 5, startedAt: SessionStart.AddHours(-1));
        await SeedRowAsync("LU0000000063", IsinProgressState.Processing, step: 6, startedAt: SessionStart.AddMinutes(5));
        await SeedRowAsync("LU0000000064", IsinProgressState.Free, step: 6, startedAt: null);
        await SeedRowAsync("LU0000000065", IsinProgressState.Processing, step: 6, startedAt: null);
        var sut = _fixture.Create<RepositoryBackedIsinProgressStore>();

        // Act
        var orphaned = await sut.ListOrphanedStep06Async(SessionStart);

        // Assert
        Assert.That(orphaned.Select(r => r.Isin), Is.EquivalentTo(new[] { "LU0000000061", "LU0000000065" }));
    }

    private Task SeedRowAsync(string isin, IsinProgressState state, int step, DateTimeOffset? startedAt)
        => _repo.UpsertAsync(new IsinProgressEntity
        {
            PartitionKey = Partition,
            RowKey = isin,
            Isin = isin,
            State = state,
            RunId = new PipelineRunId($"run-{isin}"),
            NavDate = Date2,
            CurrentStep = step,
            ProcessingStartedAt = startedAt,
        });

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
