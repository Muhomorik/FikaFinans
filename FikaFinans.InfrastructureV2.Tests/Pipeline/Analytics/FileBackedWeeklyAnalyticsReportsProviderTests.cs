using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Analytics;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Analytics;

[TestFixture]
[TestOf(typeof(FileBackedWeeklyAnalyticsReportsProvider))]
public sealed class FileBackedWeeklyAnalyticsReportsProviderTests
{
    // The week the fixture reports are exported for.
    private static readonly IsoWeek FixtureWeek = IsoWeek.From("2026-W17");

    private IFixture _fixture = null!;
    private string _dir = null!;
    private string _weeklySummaryPath = null!;
    private string _substitutionChainPath = null!;
    private string _rotationTargetsPath = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // A folder no other test shares; each test copies in only the reports it needs.
        _dir = Path.Combine(Path.GetTempPath(), $"weekly-analytics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _weeklySummaryPath     = Path.Combine(_dir, "analytics-weekly-summary.json");
        _substitutionChainPath = Path.Combine(_dir, "analytics-substitution-chain.json");
        _rotationTargetsPath   = Path.Combine(_dir, "analytics-rotation-targets.json");

        var paths = _fixture.Freeze<Mock<IPathsService>>();
        paths.Setup(x => x.AnalyticsWeeklySummaryJson).Returns(_weeklySummaryPath);
        paths.Setup(x => x.AnalyticsSubstitutionChainJson).Returns(_substitutionChainPath);
        paths.Setup(x => x.AnalyticsRotationTargetsJson).Returns(_rotationTargetsPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private FileBackedWeeklyAnalyticsReportsProvider CreateSut()
        => _fixture.Create<FileBackedWeeklyAnalyticsReportsProvider>();

    private void CopyFixtureReports()
    {
        File.Copy(Paths.AnalyticsWeeklySummaryJsonAbs, _weeklySummaryPath);
        File.Copy(Paths.AnalyticsSubstitutionChainJsonAbs, _substitutionChainPath);
        File.Copy(Paths.AnalyticsRotationTargetsJsonAbs, _rotationTargetsPath);
    }

    [Test]
    public async Task GetLatestAsync_AllThreePresentForTheWeek_ReturnsTheLinkedRuns()
    {
        // Arrange
        CopyFixtureReports();

        // Act
        var reports = await CreateSut().GetLatestAsync(FixtureWeek);

        // Assert — the fixture is a consistent export, so the runId chain links up.
        Assert.That(reports, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reports!.WeeklySummary.PeriodIsoWeek, Is.EqualTo(FixtureWeek.Value));
            Assert.That(reports.SubstitutionChain.WeeklySummaryRunId, Is.EqualTo(reports.WeeklySummary.RunId));
            Assert.That(reports.RotationTargets.SubstitutionChainRunId, Is.EqualTo(reports.SubstitutionChain.RunId));
        });
    }

    [Test]
    public async Task GetLatestAsync_FilesHoldAnEarlierWeek_ReturnsThem()
    {
        // Arrange — a stale export is still the latest there is; the caller reads its week.
        CopyFixtureReports();

        // Act
        var reports = await CreateSut().GetLatestAsync(IsoWeek.From("2026-W41"));

        // Assert
        Assert.That(reports!.WeeklySummary.PeriodIsoWeek, Is.EqualTo(FixtureWeek.Value));
    }

    [Test]
    public async Task GetLatestAsync_FilesHoldALaterWeek_ReturnsNull()
    {
        // Arrange — a fund is never analysed against a week it has not reached.
        CopyFixtureReports();

        // Act
        var reports = await CreateSut().GetLatestAsync(IsoWeek.From("2026-W16"));

        // Assert
        Assert.That(reports, Is.Null);
    }

    [Test]
    public async Task GetLatestAsync_NoReports_ReturnsNull()
    {
        // Act
        var reports = await CreateSut().GetLatestAsync(FixtureWeek);

        // Assert
        Assert.That(reports, Is.Null);
    }

    [Test]
    public async Task GetLatestAsync_OneReportMissing_ReturnsNull()
    {
        // Arrange — a partial set counts as none.
        CopyFixtureReports();
        File.Delete(_rotationTargetsPath);

        // Act
        var reports = await CreateSut().GetLatestAsync(FixtureWeek);

        // Assert
        Assert.That(reports, Is.Null);
    }

    [Test]
    public async Task GetLatestAsync_ReportReplacedBetweenCalls_ReadsTheNewOne()
    {
        // Arrange — a new export lands in the same files, so it must show up on the next
        // fund without a restart.
        CopyFixtureReports();
        var sut = CreateSut();
        var first = await sut.GetLatestAsync(FixtureWeek);
        var json = await File.ReadAllTextAsync(_weeklySummaryPath);
        await File.WriteAllTextAsync(_weeklySummaryPath, json.Replace(first!.WeeklySummary.RunId, "replaced-run-id"));

        // Act
        var reports = await sut.GetLatestAsync(FixtureWeek);

        // Assert
        Assert.That(reports!.WeeklySummary.RunId.Value, Is.EqualTo("replaced-run-id"));
    }

    [Test]
    public void GetLatestAsync_MalformedJson_ThrowsInvalidDataException()
    {
        // Arrange
        CopyFixtureReports();
        File.WriteAllText(_substitutionChainPath, "{ not json");

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().GetLatestAsync(FixtureWeek));
    }

    [Test]
    public void GetLatestAsync_JsonNull_ThrowsInvalidDataException()
    {
        // Arrange
        CopyFixtureReports();
        File.WriteAllText(_weeklySummaryPath, "null");

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().GetLatestAsync(FixtureWeek));
    }
}
