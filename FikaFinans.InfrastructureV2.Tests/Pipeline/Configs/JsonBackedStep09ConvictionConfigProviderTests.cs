using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Configs;

[TestFixture]
[TestOf(typeof(JsonBackedStep09ConvictionConfigProvider))]
public sealed class JsonBackedStep09ConvictionConfigProviderTests
{
    private IFixture _fixture = null!;
    private Mock<IPathsService> _paths = null!;
    private string _configPath = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // A path no other test shares; the file itself is written only by the tests that
        // need one.
        _configPath = Path.Combine(Path.GetTempPath(), $"config-09-conviction-{Guid.NewGuid():N}.json");

        _paths = _fixture.Freeze<Mock<IPathsService>>();
        _paths.Setup(x => x.Config09ConvictionJson).Returns(_configPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private JsonBackedStep09ConvictionConfigProvider CreateSut()
        => _fixture.Create<JsonBackedStep09ConvictionConfigProvider>();

    [Test]
    public async Task GetConfigAsync_FilePresent_ReadsItsValues()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, """
            {
              "weights": {
                "signal_strength": 0.40,
                "metrics_quality": 0.10,
                "macro_alignment": 0.15,
                "thesis_validity": 0.20,
                "universe_context": 0.15
              },
              "metrics_quality_components": { "sharpe_12w_normalization_max": 3.0 },
              "alternatives": { "max_per_fund": 5 }
            }
            """);

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Weights.SignalStrength, Is.EqualTo(0.40m));
            Assert.That(config.Weights.MetricsQuality, Is.EqualTo(0.10m));
            Assert.That(config.MetricsQualityComponents.Sharpe12wNormalizationMax, Is.EqualTo(3.0m));
            Assert.That(config.Alternatives.MaxPerFund, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task GetConfigAsync_ContractFile_ReadsTheDefaultValues()
    {
        // Arrange — the contract's file carries "_doc" keys and snake_case names with digits;
        // every value must land on the property it names.
        _paths.Setup(x => x.Config09ConvictionJson).Returns(Paths.Config09ConvictionJsonAbs);
        var defaults = UniverseEnricherConfig.Default;

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Weights.Sum(), Is.EqualTo(1.0m));
            Assert.That(config.Weights.ThesisValidity, Is.EqualTo(defaults.Weights.ThesisValidity));
            Assert.That(config.WeightValidation.Tolerance, Is.EqualTo(defaults.WeightValidation.Tolerance));
            Assert.That(config.MetricsQualityComponents.Sharpe12wNormalizationMax,
                Is.EqualTo(defaults.MetricsQualityComponents.Sharpe12wNormalizationMax));
            Assert.That(config.MetricsQualityComponents.DrawdownPenaltyThresholdPct,
                Is.EqualTo(defaults.MetricsQualityComponents.DrawdownPenaltyThresholdPct));
            Assert.That(config.Alternatives.DifferentiatorDimensions,
                Is.EqualTo(defaults.Alternatives.DifferentiatorDimensions));
            Assert.That(config.UniverseRankWithin, Is.EqualTo(defaults.UniverseRankWithin));
        });
    }

    [Test]
    public async Task GetConfigAsync_FileMissing_ReturnsDefaults()
    {
        // Arrange
        var defaults = UniverseEnricherConfig.Default;

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Weights.SignalStrength, Is.EqualTo(defaults.Weights.SignalStrength));
            Assert.That(config.Weights.Sum(), Is.EqualTo(defaults.Weights.Sum()));
            Assert.That(config.Alternatives.MaxPerFund, Is.EqualTo(defaults.Alternatives.MaxPerFund));
        });
    }

    [Test]
    public async Task GetConfigAsync_WeightsNotSummingToOne_ReturnsThemUnchecked()
    {
        // Arrange — the weights check stays in the agent, so a bad edit fails the run, not
        // the read.
        await File.WriteAllTextAsync(_configPath, """{ "weights": { "signal_strength": 0.90 } }""");

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.That(config.Weights.SignalStrength, Is.EqualTo(0.90m));
    }

    [Test]
    public async Task GetConfigAsync_FileEditedBetweenCalls_ReadsTheEdit()
    {
        // Arrange — the tab's config editor writes the same file, so an edit must show up
        // on the next run without a restart.
        var sut = CreateSut();
        await File.WriteAllTextAsync(_configPath, """{ "alternatives": { "max_per_fund": 2 } }""");
        await sut.GetConfigAsync();
        await File.WriteAllTextAsync(_configPath, """{ "alternatives": { "max_per_fund": 4 } }""");

        // Act
        var config = await sut.GetConfigAsync();

        // Assert
        Assert.That(config.Alternatives.MaxPerFund, Is.EqualTo(4));
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
