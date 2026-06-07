using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;
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

        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId));

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            foreach (var row in rows)
            {
                Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing));
                Assert.That(row.RunId, Is.EqualTo(new PipelineRunId(_runId)));
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
            RunId = new PipelineRunId("old-run"),
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

        await sut.ClaimIsinProgressAsync(MakeStep1Output("LU0000000001"), new PipelineRunId(_runId));

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.RunId, Is.EqualTo(new PipelineRunId(_runId)));
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
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId));

        var block = new PerIsinBlockResult(
            Step2Output: MakeStep1Output("LU0000000001"),
            Step4Output: MakeStep1Output("LU0000000001"),
            Step5Output: MakeStep1Output("LU0000000001"),
            Step6Output: MakeStep1Output("LU0000000001"),
            Step7Output: MakeStep1Output("LU0000000001"),
            Step8Output: MakeStep1Output("LU0000000001"),
            FailedFunds: new Dictionary<string, string>());

        await sut.WriteIsinProgressBlockAsync(block, new PipelineRunId(_runId));

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
    public async Task WriteIsinProgressStep9Async_PopulatesStep09JsonFromInMemoryOutput()
    {
        // Phase 8 sub-step 8a: caller threads Step 9's in-memory
        // DataLoaderOutput through; no disk round-trip.
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId));

        var step9Output = MakeStep1Output("LU0000000001");

        await sut.WriteIsinProgressStep9Async(step9Output, new PipelineRunId(_runId));

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.CurrentStep, Is.EqualTo(9));
            Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing));
            Assert.That(row.Step09Json, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void WriteIsinProgressStep9Async_NullOutput_ThrowsArgumentNullException()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.WriteIsinProgressStep9Async(null!, new PipelineRunId(_runId)));
    }

    [Test]
    public async Task ReleaseIsinProgressAsync_FlipsStateToFreeAndClearsProcessingStartedAt()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001", "LU0000000002");
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId));

        await sut.ReleaseIsinProgressAsync(step1, new PipelineRunId(_runId));

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                Assert.That(row.State, Is.EqualTo(IsinProgressState.Free));
                Assert.That(row.ProcessingStartedAt, Is.Null);
                Assert.That(row.RunId, Is.EqualTo(new PipelineRunId(_runId)), "RunId is preserved as the latest-run record");
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

        await sut.ReleaseIsinProgressAsync(step1, new PipelineRunId(_runId));

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task ClaimIsinProgressAsync_WithNavDateMap_WritesNavDateOnEachRow()
    {
        // The triggering navDate per ISIN is threaded in at claim time and
        // recorded as the in-flight NavDate (today it was always left null).
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001", "LU0000000002");
        var navDate1 = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
        var navDate2 = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        var navDates = new Dictionary<string, DateTimeOffset>
        {
            ["LU0000000001"] = navDate1,
            ["LU0000000002"] = navDate2,
        };

        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId), navDates);

        var row1 = await _repo.GetAsync(Partition, "LU0000000001");
        var row2 = await _repo.GetAsync(Partition, "LU0000000002");
        Assert.Multiple(() =>
        {
            Assert.That(row1!.NavDate, Is.EqualTo(navDate1));
            Assert.That(row2!.NavDate, Is.EqualTo(navDate2));
        });
    }

    [Test]
    public async Task ReleaseIsinProgressAsync_SucceededFund_AdvancesLatestProcessedNavDateToNavDate()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");
        var navDate = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId),
            new Dictionary<string, DateTimeOffset> { ["LU0000000001"] = navDate });

        // Empty failed set → the fund succeeded → anchor advances to NavDate.
        await sut.ReleaseIsinProgressAsync(step1, new PipelineRunId(_runId), failedIsins: new HashSet<string>());

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free));
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(navDate),
                "succeeded fund advances the dedup anchor to the run's NavDate");
        });
    }

    [Test]
    public async Task ReleaseIsinProgressAsync_FailedFund_LeavesLatestProcessedNavDateUnchanged()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");

        // A first successful run stamps the anchor at date1.
        var date1 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId),
            new Dictionary<string, DateTimeOffset> { ["LU0000000001"] = date1 });
        await sut.ReleaseIsinProgressAsync(step1, new PipelineRunId(_runId), failedIsins: new HashSet<string>());

        // A later run for a newer date2 fails for this fund.
        var date2 = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId),
            new Dictionary<string, DateTimeOffset> { ["LU0000000001"] = date2 });
        await sut.ReleaseIsinProgressAsync(step1, new PipelineRunId(_runId),
            failedIsins: new HashSet<string> { "LU0000000001" });

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(IsinProgressState.Free), "row is still released to Free");
            Assert.That(row.LatestProcessedNavDate, Is.EqualTo(date1),
                "failed fund keeps the prior anchor so the next signal re-raises it");
        });
    }

    [Test]
    public async Task MarkFundFailedAsync_SetsLastErrorBumpsAttemptCountAndPreservesColumns()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        await sut.ClaimIsinProgressAsync(MakeStep1Output("LU0000000001"), new PipelineRunId(_runId));

        await sut.MarkFundFailedAsync("LU0000000001", new PipelineRunId(_runId), "Step 7 exploded");

        var row = await _repo.GetAsync(Partition, "LU0000000001");
        Assert.Multiple(() =>
        {
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.LastError, Is.EqualTo("Step 7 exploded"));
            Assert.That(row.AttemptCount, Is.EqualTo(1), "AttemptCount bumps on each MarkFundFailed call");
            Assert.That(row.RunId, Is.EqualTo(new PipelineRunId(_runId)));
            Assert.That(row.State, Is.EqualTo(IsinProgressState.Processing),
                "State is left Processing — Release flips it to Free at end of run");
            Assert.That(row.Step01Json, Is.Not.Null, "Step01Json from Claim is preserved");
        });
    }

    [Test]
    public async Task LoadUniverseFromIsinProgressAsync_Step8Source_AssemblesFromStep08JsonColumns()
    {
        // 8b: seed two funds through Claim + Block (which populates Step08Json
        // per fund), then verify the gateway reassembles a DataLoaderOutput
        // whose universe-wide fields come from the template and whose Funds
        // come from the Step08Json column round-trip.
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001", "LU0000000002");
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId));

        var block = new PerIsinBlockResult(
            Step2Output: MakeStep1Output("LU0000000001", "LU0000000002"),
            Step4Output: MakeStep1Output("LU0000000001", "LU0000000002"),
            Step5Output: MakeStep1Output("LU0000000001", "LU0000000002"),
            Step6Output: MakeStep1Output("LU0000000001", "LU0000000002"),
            Step7Output: MakeStep1Output("LU0000000001", "LU0000000002"),
            Step8Output: MakeStep1Output("LU0000000001", "LU0000000002"),
            FailedFunds: new Dictionary<string, string>());
        await sut.WriteIsinProgressBlockAsync(block, new PipelineRunId(_runId));

        var assembled = await sut.LoadUniverseFromIsinProgressAsync(step1, StepId.Recommender);

        Assert.Multiple(() =>
        {
            Assert.That(assembled, Is.Not.Null);
            Assert.That(assembled.IsoWeek, Is.EqualTo(step1.IsoWeek), "universe-wide IsoWeek comes from template");
            Assert.That(assembled.RunId, Is.EqualTo(step1.RunId), "universe-wide RunId comes from template");
            Assert.That(assembled.Family, Is.EqualTo(step1.Family));
            Assert.That(assembled.Funds, Has.Count.EqualTo(2));
            Assert.That(assembled.Funds.Select(f => f.Isin.Value),
                Is.EqualTo(new[] { "LU0000000001", "LU0000000002" }),
                "fund order matches template order");
        });
    }

    [Test]
    public async Task LoadUniverseFromIsinProgressAsync_Step9Source_AssemblesFromStep09JsonColumns()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");
        await sut.ClaimIsinProgressAsync(step1, new PipelineRunId(_runId));
        await sut.WriteIsinProgressStep9Async(MakeStep1Output("LU0000000001"), new PipelineRunId(_runId));

        var assembled = await sut.LoadUniverseFromIsinProgressAsync(step1, StepId.UniverseEnricher);

        Assert.Multiple(() =>
        {
            Assert.That(assembled.Funds, Has.Count.EqualTo(1));
            Assert.That(assembled.Funds[0].Isin.Value, Is.EqualTo("LU0000000001"));
        });
    }

    [Test]
    public async Task LoadUniverseFromIsinProgressAsync_FundMissingFromPartition_IsDropped()
    {
        // Template has two funds but only one was claimed → only one row exists
        // in SQLite. The assembled output should contain just the surviving fund.
        var sut = _fixture.Create<StreamingPipelineGateway>();
        await sut.ClaimIsinProgressAsync(MakeStep1Output("LU0000000001"), new PipelineRunId(_runId));
        var block = new PerIsinBlockResult(
            Step2Output: MakeStep1Output("LU0000000001"),
            Step4Output: MakeStep1Output("LU0000000001"),
            Step5Output: MakeStep1Output("LU0000000001"),
            Step6Output: MakeStep1Output("LU0000000001"),
            Step7Output: MakeStep1Output("LU0000000001"),
            Step8Output: MakeStep1Output("LU0000000001"),
            FailedFunds: new Dictionary<string, string>());
        await sut.WriteIsinProgressBlockAsync(block, new PipelineRunId(_runId));

        var templateWithMissingFund = MakeStep1Output("LU0000000001", "LU0000000099");
        var assembled = await sut.LoadUniverseFromIsinProgressAsync(templateWithMissingFund, StepId.Recommender);

        Assert.That(assembled.Funds, Has.Count.EqualTo(1));
        Assert.That(assembled.Funds[0].Isin.Value, Is.EqualTo("LU0000000001"));
    }

    [Test]
    public void LoadUniverseFromIsinProgressAsync_NullTemplate_ThrowsArgumentNullException()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.LoadUniverseFromIsinProgressAsync(null!, StepId.Recommender));
    }

    [Test]
    public void LoadUniverseFromIsinProgressAsync_UnsupportedStep_ThrowsArgumentOutOfRangeException()
    {
        // Only Step 8 / Step 9 are legal sources. Asking for Step 2 should fail
        // loudly rather than return an empty universe.
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var step1 = MakeStep1Output("LU0000000001");

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.LoadUniverseFromIsinProgressAsync(step1, StepId.MetricsCalculator));
    }

    [Test]
    public async Task MarkFundFailedAsync_MissingRow_IsNoOp()
    {
        // Standalone RunPerIsinBlockAsync callers (tests) never Claim, so the
        // row may not exist. The gateway should swallow that case rather than
        // surfacing a KeyNotFound / NullReference equivalent.
        var sut = _fixture.Create<StreamingPipelineGateway>();

        await sut.MarkFundFailedAsync("LU0000000099", new PipelineRunId(_runId), "anything");

        var rows = await _repo.QueryPartitionAsync(Partition);
        Assert.That(rows, Is.Empty);
    }

    private static DataLoaderOutput MakeStep1Output(params string[] isins) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = IsoWeek,
        Family          = "synthetic",
        RunId           = new PipelineRunId("test-run"),
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
