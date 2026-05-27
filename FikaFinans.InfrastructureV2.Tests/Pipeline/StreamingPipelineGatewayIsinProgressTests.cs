using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Infrastructure.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(StreamingPipelineGateway))]
public sealed class StreamingPipelineGatewayIsinProgressTests
{
    private const string IsoWeek = "2026-W21";
    private const string Partition = "isin-progress";

    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;
    private SqliteIsinProgressRepository _repo = null!;
    private string _runId = null!;
    private List<string> _filesToCleanup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());

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

        _runId = $"isin-progress-it-{Guid.NewGuid():N}";
        _filesToCleanup = new List<string>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _filesToCleanup)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        _connection.Dispose();
    }

    [Test]
    public async Task ClaimIsinProgressAsync_PersistsRowsAsProcessingWithStep01JsonFilled()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001", "LU0000000002");

        await sut.ClaimIsinProgressAsync(step1, _runId);

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            foreach (var row in rows)
            {
                Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing));
                Assert.That(row.RunId, Is.EqualTo(_runId));
                Assert.That(row.CurrentStep, Is.EqualTo(1));
                Assert.That(row.ProcessingStartedAt, Is.Not.Null);
                Assert.That(row.Step01Json, Is.Not.Null.And.Not.Empty);
                Assert.That(row.Step02Json, Is.Null);
                Assert.That(row.Step03Json, Is.Null);
                Assert.That(row.Step09Json, Is.Null);
            }
        });
    }

    [Test]
    public async Task ClaimIsinProgressAsync_ClearsPriorRunColumns()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();

        // Seed a prior-run row with every column populated.
        await _repo.UpsertAsync(new Application.Storage.Bank.Entities.IsinProgressEntity
        {
            PartitionKey = Partition,
            RowKey = "LU0000000001",
            Isin = "LU0000000001",
            State = IsinProgressState.Free,
            RunId = "old-run",
            CurrentStep = 9,
            Step01Json = "old1",
            Step02Json = "old2",
            Step04Json = "old4",
            Step05Json = "old5",
            Step06Json = "old6",
            Step07Json = "old7",
            Step08Json = "old8",
            Step09Json = "old9",
        });

        await sut.ClaimIsinProgressAsync(MakeStep1Output("LU0000000001"), _runId);

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.RunId, Is.EqualTo(_runId));
            Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing));
            Assert.That(row.CurrentStep, Is.EqualTo(1));
            Assert.That(row.Step01Json, Is.Not.Null.And.Not.EqualTo("old1"));
            Assert.That(row.Step02Json, Is.Null);
            Assert.That(row.Step04Json, Is.Null);
            Assert.That(row.Step05Json, Is.Null);
            Assert.That(row.Step06Json, Is.Null);
            Assert.That(row.Step07Json, Is.Null);
            Assert.That(row.Step08Json, Is.Null);
            Assert.That(row.Step09Json, Is.Null);
        });
    }

    [Test]
    public async Task WriteIsinProgressBlockAsync_PopulatesStep02ThroughStep08LeavingStep03Null()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");
        await sut.ClaimIsinProgressAsync(step1, _runId);

        var block = new PerIsinBlockResult(
            Step2Output: MakeStep1Output("LU0000000001"),
            Step4Output: MakeStep1Output("LU0000000001"),
            Step5Output: MakeStep1Output("LU0000000001"),
            Step6Output: MakeStep1Output("LU0000000001"),
            Step7Output: MakeStep1Output("LU0000000001"),
            Step8Output: MakeStep1Output("LU0000000001"),
            FailedFunds: new Dictionary<string, string>());

        await sut.WriteIsinProgressBlockAsync(block, _runId);

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.CurrentStep, Is.EqualTo(8));
            Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing));
            Assert.That(row.Step01Json, Is.Not.Null);
            Assert.That(row.Step02Json, Is.Not.Null);
            Assert.That(row.Step03Json, Is.Null, "Step 3 is universe-wide; no per-ISIN slice");
            Assert.That(row.Step04Json, Is.Not.Null);
            Assert.That(row.Step05Json, Is.Not.Null);
            Assert.That(row.Step06Json, Is.Not.Null);
            Assert.That(row.Step07Json, Is.Not.Null);
            Assert.That(row.Step08Json, Is.Not.Null);
            Assert.That(row.Step09Json, Is.Null);
        });
    }

    [Test]
    public async Task WriteIsinProgressStep9Async_LoadsFromDiskAndPopulatesStep09Json()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");
        await sut.ClaimIsinProgressAsync(step1, _runId);

        var paths = new TestPathsService();
        var step9Path = paths.UniverseEnricherOutput(IsoWeek, _runId);
        _filesToCleanup.Add(step9Path);
        Directory.CreateDirectory(Path.GetDirectoryName(step9Path)!);
        File.WriteAllText(step9Path, JsonSerializer.Serialize(
            MakeStep1Output("LU0000000001"), JsonOptions.Default));

        await sut.WriteIsinProgressStep9Async(IsoWeek, _runId);

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.CurrentStep, Is.EqualTo(9));
            Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing));
            Assert.That(row.Step09Json, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task ReleaseIsinProgressAsync_FlipsStateToFreeAndClearsProcessingStartedAt()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001", "LU0000000002");
        await sut.ClaimIsinProgressAsync(step1, _runId);

        await sut.ReleaseIsinProgressAsync(step1, _runId);

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                Assert.That(row.State, Is.EqualTo(IsinProgressState.Free));
                Assert.That(row.ProcessingStartedAt, Is.Null);
                Assert.That(row.RunId, Is.EqualTo(_runId), "RunId is preserved as the latest-run record");
                Assert.That(row.Step01Json, Is.Not.Null, "step columns survive the release");
            }
        });
    }

    [Test]
    public async Task ReleaseIsinProgressAsync_SkipsMissingRows()
    {
        // Release without a prior claim — repo is empty. Should be a no-op,
        // not throw on a non-existent row.
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");

        await sut.ReleaseIsinProgressAsync(step1, _runId);

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task MarkFundFailedAsync_SetsLastErrorBumpsAttemptCountAndPreservesColumns()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        await sut.ClaimIsinProgressAsync(MakeStep1Output("LU0000000001"), _runId);

        await sut.MarkFundFailedAsync("LU0000000001", _runId, "Step 7 exploded");

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.LastError, Is.EqualTo("Step 7 exploded"));
            Assert.That(row.AttemptCount, Is.EqualTo(1), "AttemptCount bumps on each MarkFundFailed call");
            Assert.That(row.RunId, Is.EqualTo(_runId));
            Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing),
                "State is left Processing — Release flips it to Free at end of run");
            Assert.That(row.Step01Json, Is.Not.Null, "Step01Json from Claim is preserved");
        });
    }

    [Test]
    public async Task MarkFundFailedAsync_MissingRow_IsNoOp()
    {
        // Standalone RunPerIsinBlockAsync callers (tests) never Claim, so the
        // row may not exist. The gateway should swallow that case rather than
        // surfacing a KeyNotFound / NullReference equivalent.
        var sut = _fixture.Create<StreamingPipelineGateway>();

        await sut.MarkFundFailedAsync("LU0000000099", _runId, "anything");

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.That(rows, Is.Empty);
    }

    private static DataLoaderOutput MakeStep1Output(params string[] isins) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = IsoWeek,
        Family          = "synthetic",
        RunId           = "test-run",
        ConfigVersion   = "1.0.0",
        Funds           = isins.Select(MakeFund).ToList(),
        FrozenPositions = Array.Empty<FrozenPosition>(),
        CashAvailableKr = 0m,
        DataQuality     = new DataQuality(),
    };

    private static FundRecord MakeFund(string isin) => new()
    {
        Isin           = isin,
        Metadata       = new FundMetadata
        {
            Isin                     = isin,
            Name                     = $"Fund {isin}",
            CompanyName              = "TestCo",
            CurrencyCode             = "SEK",
            Category                 = "Globalfond",
            FundType                 = "EQUITY_FUND",
            IsIndexFund              = false,
            ManagedType              = "ACTIVE",
            TotalFee                 = 1.0m,
            ManagementFee            = 0.7m,
            Risk                     = 4,
            Rating                   = 3,
            SharpeRatioStatic        = 1.0m,
            StandardDeviationStatic  = 12.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
            Capital                  = 1_000_000m,
            NumberOfOwners           = 100,
        },
        NavBuckets     = Array.Empty<NavBucket>(),
        Snapshot       = null,
        CurrentlyHeld  = false,
        CurrentValueKr = null,
        CostBasisKr    = null,
        Layer          = FundLayer.Active,
        Metrics        = null,
    };
}
