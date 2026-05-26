using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
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
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var output = MakeOutput(_runId, "LU0000000001", "LU0000000002");
        var paths = new TestPathsService();

        var perIsinSteps = new (StepId step, string path)[]
        {
            (StepId.MetricsCalculator, paths.MetricsCalculatorOutput(IsoWeek, _runId)),
            (StepId.SignalScorer,      paths.SignalScorerOutput(IsoWeek, _runId)),
            (StepId.MacroAligner,      paths.MacroAlignerOutput(IsoWeek, _runId)),
            (StepId.CatalystTagger,    paths.CatalystTaggerOutput(IsoWeek, _runId)),
            (StepId.ThesisValidator,   paths.ThesisValidatorOutput(IsoWeek, _runId)),
            (StepId.Recommender,       paths.RecommenderOutput(IsoWeek, _runId)),
        };

        foreach (var (step, path) in perIsinSteps)
        {
            _filesToCleanup.Add(path);
            sut.SaveStepOutput(step, IsoWeek, _runId, output);

            Assert.That(File.Exists(path), Is.True, $"{step}: expected output file at {path}");

            var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
                File.ReadAllText(path), JsonOptions.Default);
            Assert.That(roundTripped, Is.Not.Null, $"{step}: deserialized output should not be null");
            Assert.That(roundTripped!.Funds, Has.Count.EqualTo(2), $"{step}: fund count preserved");
            Assert.That(roundTripped.IsoWeek, Is.EqualTo(IsoWeek), $"{step}: isoWeek preserved");
            Assert.That(roundTripped.RunId, Is.EqualTo(_runId), $"{step}: runId preserved");
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
                () => sut.SaveStepOutput(StepId.DataLoader, IsoWeek, _runId, output));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.MacroAnalyst, IsoWeek, _runId, output));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.UniverseEnricher, IsoWeek, _runId, output));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sut.SaveStepOutput(StepId.PortfolioConstructor, IsoWeek, _runId, output));
        });
    }

    [Test]
    public void LoadStep1Output_ReadsBackWhatWasWritten()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var paths = new TestPathsService();

        var step1Path = paths.DataLoaderOutput(IsoWeek, _runId);
        _filesToCleanup.Add(step1Path);

        var written = MakeOutput(_runId, "LU0000000001", "LU0000000002", "LU0000000003");
        Directory.CreateDirectory(Path.GetDirectoryName(step1Path)!);
        File.WriteAllText(step1Path, JsonSerializer.Serialize(written, JsonOptions.Default));

        var loaded = sut.LoadStep1Output(IsoWeek, _runId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Funds, Has.Count.EqualTo(3));
            Assert.That(loaded.Funds.Select(f => f.Isin.Value),
                Is.EqualTo(new[] { "LU0000000001", "LU0000000002", "LU0000000003" }));
            Assert.That(loaded.RunId, Is.EqualTo(_runId));
        });
    }

    [Test]
    public void LoadStep3Output_ReadsBackWhatWasWritten()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();
        var paths = new TestPathsService();

        var step3Path = paths.MacroAnalystOutput(IsoWeek, _runId);
        _filesToCleanup.Add(step3Path);

        var written = MakeMacroContext();
        Directory.CreateDirectory(Path.GetDirectoryName(step3Path)!);
        File.WriteAllText(step3Path, JsonSerializer.Serialize(written, JsonOptions.Default));

        var loaded = sut.LoadStep3Output(IsoWeek, _runId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.IsoWeek, Is.EqualTo(IsoWeek));
            Assert.That(loaded.MacroRegime, Is.EqualTo(MacroRegime.Mixed));
        });
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

    [Test]
    public void LoadStep1Output_FileMissing_Throws()
    {
        var sut = _fixture.Create<StreamingPipelineGateway>();

        Assert.Throws<FileNotFoundException>(
            () => sut.LoadStep1Output(IsoWeek, $"missing-{Guid.NewGuid():N}"));
    }

    private static DataLoaderOutput MakeOutput(string runId, params string[] isins) => new()
    {
        GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek         = IsoWeek,
        Family          = "schroder",
        RunId           = runId,
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

    private static MacroContext MakeMacroContext() => new()
    {
        GeneratedAt   = DateTimeOffset.UtcNow.ToString("o"),
        IsoWeek       = IsoWeek,
        ConfigVersion = "1.0.0",
        SourceRunIds  = new SourceRunIds
        {
            WeeklySummaryRunId     = "synthetic-ws",
            SubstitutionChainRunId = "synthetic-sc",
            RotationTargetsRunId   = "synthetic-rt",
        },
        MacroRegime      = MacroRegime.Mixed,
        RegimeConfidence = 0.5m,
        NetMoodInput     = MarketSentiment.Mixed,
        Catalysts        = Array.Empty<Catalyst>(),
        RotationThemes   = Array.Empty<RotationTheme>(),
        Warnings         = null,
    };
}
