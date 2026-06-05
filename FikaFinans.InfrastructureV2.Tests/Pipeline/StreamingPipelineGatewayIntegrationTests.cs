using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(StreamingPipelineGateway))]
public sealed class StreamingPipelineGatewayIntegrationTests
{
    private const string IsoWeek = "2026-W21";

    private IFixture _fixture = null!;
    private string _runId = null!;
    private List<string> _filesToCleanup = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());

        // Unique per test so disk writes don't collide with sibling tests or
        // leftover artifacts from previous runs.
        _runId = $"streaming-it-{Guid.NewGuid():N}";
        _filesToCleanup = new List<string>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _filesToCleanup)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Test]
    public void SaveStepOutput_PerIsinSteps_RoundTripsThroughDisk()
    {
        // 8d flipped the default to false — this test exercises the opt-in
        // disk-write path, so it must explicitly enable the flag.
        _fixture.Inject(new StreamingPipelineOptions { WriteDiskJsonArtifacts = true });
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var output = MakeOutput(_runId, "LU0000000001", "LU0000000002");
        var paths = new TestPathsService();

        var perIsinSteps = new (StepId step, string path)[]
        {
            (StepId.MetricsCalculator, paths.MetricsCalculatorOutput(IsoWeek, new PipelineRunId(_runId))),
            (StepId.SignalScorer,      paths.SignalScorerOutput(IsoWeek, new PipelineRunId(_runId))),
            (StepId.MacroAligner,      paths.MacroAlignerOutput(IsoWeek, new PipelineRunId(_runId))),
            (StepId.CatalystTagger,    paths.CatalystTaggerOutput(IsoWeek, new PipelineRunId(_runId))),
            (StepId.ThesisValidator,   paths.ThesisValidatorOutput(IsoWeek, new PipelineRunId(_runId))),
            (StepId.Recommender,       paths.RecommenderOutput(IsoWeek, new PipelineRunId(_runId))),
        };

        foreach (var (step, path) in perIsinSteps)
        {
            _filesToCleanup.Add(path);
            sut.SaveStepOutput(step, IsoWeek, new PipelineRunId(_runId), output);

            Assert.That(File.Exists(path), Is.True, $"{step}: expected output file at {path}");

            var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
                File.ReadAllText(path), JsonOptions.Default);
            Assert.That(roundTripped, Is.Not.Null, $"{step}: deserialized output should not be null");
            Assert.That(roundTripped!.Funds, Has.Count.EqualTo(2), $"{step}: fund count preserved");
            Assert.That(roundTripped.IsoWeek, Is.EqualTo(IsoWeek), $"{step}: isoWeek preserved");
            Assert.That(roundTripped.RunId.Value, Is.EqualTo(_runId), $"{step}: runId preserved");
        }
    }

    [Test]
    public void SaveStepOutput_UniverseWideStep_Throws()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var output = MakeOutput(_runId, "LU0000000001");

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.DataLoader, IsoWeek, new PipelineRunId(_runId), output));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.MacroAnalyst, IsoWeek, new PipelineRunId(_runId), output));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.UniverseEnricher, IsoWeek, new PipelineRunId(_runId), output));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.PortfolioConstructor, IsoWeek, new PipelineRunId(_runId), output));
        });
    }

    [Test]
    public void StreamingPipelineOptions_WriteDiskJsonArtifactsDefault_IsFalse()
    {
        // Sentinel test for 8d: if anyone flips the default back to true,
        // this fires before silent disk writes start happening again.
        var options = new StreamingPipelineOptions();

        Assert.That(options.WriteDiskJsonArtifacts, Is.False,
            "Default flipped to false on 2026-05-30 (Phase 8 sub-step 8d). " +
            "SQLite IsinProgress columns are the canonical step-output store.");
    }

    [Test]
    public void SaveStepOutput_WhenWriteDiskJsonArtifactsIsFalse_SkipsDiskWrite()
    {
        // Open Q #4 gate: with the flag flipped off the gateway must NOT
        // write the boundary JSON, even for a valid per-ISIN step. Failing
        // to honour the flag would defeat the canonical-SQLite migration's
        // future "disk readers retired" default.
        _fixture.Inject(new StreamingPipelineOptions { WriteDiskJsonArtifacts = false });
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var output = MakeOutput(_runId, "LU0000000001");
        var paths = new TestPathsService();
        var step2Path = paths.MetricsCalculatorOutput(IsoWeek, new PipelineRunId(_runId));
        _filesToCleanup.Add(step2Path);

        sut.SaveStepOutput(StepId.MetricsCalculator, IsoWeek, new PipelineRunId(_runId), output);

        Assert.That(File.Exists(step2Path), Is.False,
            "WriteDiskJsonArtifacts=false should skip the disk write");
    }

    [Test]
    public void SaveStepOutput_WhenWriteDiskJsonArtifactsIsFalse_StillThrowsForUniverseWideStep()
    {
        // The gate skips IO but does NOT silence input-validation errors —
        // callers passing the wrong step are buggy regardless of the flag.
        _fixture.Inject(new StreamingPipelineOptions { WriteDiskJsonArtifacts = false });
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var output = MakeOutput(_runId, "LU0000000001");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => sut.SaveStepOutput(StepId.DataLoader, IsoWeek, new PipelineRunId(_runId), output));
    }

    [Test]
    public void LoadMetricsConfig_RealFixturePresent_DeserializesSuccessfully()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();

        var config = sut.LoadMetricsConfig();

        Assert.That(config, Is.Not.Null);
    }

    [Test]
    public void LoadSignalConfig_RealFixturePresent_DeserializesSuccessfully()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();

        var config = sut.LoadSignalConfig();

        Assert.That(config, Is.Not.Null);
        Assert.That(config.SellWeaknessAnyOf, Is.Not.Null);
    }

    private static DataLoaderOutput MakeOutput(string runId, params string[] isins) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = IsoWeek,
        Family          = "schroder",
        RunId           = new PipelineRunId(runId),
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
