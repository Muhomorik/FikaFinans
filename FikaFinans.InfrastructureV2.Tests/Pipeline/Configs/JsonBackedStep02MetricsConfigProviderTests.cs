using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Configs;

[TestFixture]
[TestOf(typeof(JsonBackedStep02MetricsConfigProvider))]
public sealed class JsonBackedStep02MetricsConfigProviderTests
{
    private IFixture _fixture = null!;
    private string _configPath = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // A path no other test shares; the file itself is written only by the tests that
        // need one.
        _configPath = Path.Combine(Path.GetTempPath(), $"config-02-metrics-{Guid.NewGuid():N}.json");

        _fixture.Freeze<Mock<IPathsService>>()
            .Setup(x => x.Config02MetricsJson)
            .Returns(_configPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private JsonBackedStep02MetricsConfigProvider CreateSut()
        => _fixture.Create<JsonBackedStep02MetricsConfigProvider>();

    [Test]
    public async Task GetConfigAsync_FilePresent_ReadsItsValues()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, """
            {
              "stale_snapshot_warn_days": 21,
              "fee_deduction_horizon_weeks": 26
            }
            """);

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.StaleSnapshotWarnDays, Is.EqualTo(21));
            Assert.That(config.FeeDeductionHorizonWeeks, Is.EqualTo(26));
        });
    }

    [Test]
    public async Task GetConfigAsync_FileMissing_ReturnsDefaults()
    {
        // Arrange
        var defaults = MetricsCalculatorConfig.Default;

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.StaleSnapshotWarnDays, Is.EqualTo(defaults.StaleSnapshotWarnDays));
            Assert.That(config.FeeDeductionHorizonWeeks, Is.EqualTo(defaults.FeeDeductionHorizonWeeks));
        });
    }

    [Test]
    public async Task GetConfigAsync_FileEditedBetweenCalls_ReadsTheEdit()
    {
        // Arrange — the tab's config editor writes the same file, so an edit must show up
        // on the next fund without a restart.
        var sut = CreateSut();
        await File.WriteAllTextAsync(_configPath, """{ "stale_snapshot_warn_days": 21 }""");
        await sut.GetConfigAsync();
        await File.WriteAllTextAsync(_configPath, """{ "stale_snapshot_warn_days": 30 }""");

        // Act
        var config = await sut.GetConfigAsync();

        // Assert
        Assert.That(config.StaleSnapshotWarnDays, Is.EqualTo(30));
    }

    [Test]
    public async Task GetConfigAsync_MalformedJson_ThrowsInvalidDataException()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, "{ not json");

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().GetConfigAsync());
    }

    [Test]
    public async Task GetConfigAsync_JsonNull_ThrowsInvalidDataException()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, "null");

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().GetConfigAsync());
    }
}
