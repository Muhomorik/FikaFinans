using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Configs;

[TestFixture]
[TestOf(typeof(JsonBackedStep04SignalsConfigProvider))]
public sealed class JsonBackedStep04SignalsConfigProviderTests
{
    private IFixture _fixture = null!;
    private string _configPath = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // A path no other test shares; the file itself is written only by the tests that
        // need one.
        _configPath = Path.Combine(Path.GetTempPath(), $"config-04-signals-{Guid.NewGuid():N}.json");

        _fixture.Freeze<Mock<IPathsService>>()
            .Setup(x => x.Config04SignalsJson)
            .Returns(_configPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private JsonBackedStep04SignalsConfigProvider CreateSut()
        => _fixture.Create<JsonBackedStep04SignalsConfigProvider>();

    [Test]
    public async Task GetConfigAsync_FilePresent_ReadsItsValues()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, """
            {
              "buy_strength": { "min_sharpe_12w": 1.0 },
              "sell_weakness_any_of": [
                { "metric": "sharpe_2w", "comparator": "lt", "value": -0.5 }
              ],
              "data_quality_handling": { "treat_nan_sharpe_as_zero": false }
            }
            """);

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.BuyStrength.MinSharpe12w, Is.EqualTo(1.0m));
            Assert.That(config.SellWeaknessAnyOf, Has.Count.EqualTo(1));
            Assert.That(config.SellWeaknessAnyOf[0].Value, Is.EqualTo(-0.5m));
            Assert.That(config.DataQualityHandling.TreatNanSharpeAsZero, Is.False);
        });
    }

    [Test]
    public async Task GetConfigAsync_FileMissing_ReturnsDefaults()
    {
        // Arrange
        var defaults = SignalScorerConfig.Default;

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.BuyStrength.MinSharpe12w, Is.EqualTo(defaults.BuyStrength.MinSharpe12w));
            Assert.That(config.SellWeaknessAnyOf, Is.EqualTo(SellTrigger.Defaults));
            Assert.That(config.DataQualityHandling.TreatNanSharpeAsZero,
                Is.EqualTo(defaults.DataQualityHandling.TreatNanSharpeAsZero));
        });
    }

    [Test]
    public async Task GetConfigAsync_SellTriggersEmpty_FallsBackToDefaultTriggers()
    {
        // Arrange — the contract's sell rule is any one of three triggers; an empty list
        // would silently switch every exit off.
        await File.WriteAllTextAsync(_configPath, """
            {
              "buy_strength": { "min_sharpe_12w": 1.0 },
              "sell_weakness_any_of": []
            }
            """);

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert — the rest of the file still applies.
        Assert.Multiple(() =>
        {
            Assert.That(config.SellWeaknessAnyOf, Is.EqualTo(SellTrigger.Defaults));
            Assert.That(config.BuyStrength.MinSharpe12w, Is.EqualTo(1.0m));
        });
    }

    [Test]
    public async Task GetConfigAsync_SellTriggersNull_FallsBackToDefaultTriggers()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, """{ "sell_weakness_any_of": null }""");

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.That(config.SellWeaknessAnyOf, Is.EqualTo(SellTrigger.Defaults));
    }

    [Test]
    public async Task GetConfigAsync_FileEditedBetweenCalls_ReadsTheEdit()
    {
        // Arrange — the tab's config editor writes the same file, so an edit must show up
        // on the next fund without a restart.
        var sut = CreateSut();
        await File.WriteAllTextAsync(_configPath, """{ "buy_strength": { "min_sharpe_12w": 1.0 } }""");
        await sut.GetConfigAsync();
        await File.WriteAllTextAsync(_configPath, """{ "buy_strength": { "min_sharpe_12w": 2.0 } }""");

        // Act
        var config = await sut.GetConfigAsync();

        // Assert
        Assert.That(config.BuyStrength.MinSharpe12w, Is.EqualTo(2.0m));
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
