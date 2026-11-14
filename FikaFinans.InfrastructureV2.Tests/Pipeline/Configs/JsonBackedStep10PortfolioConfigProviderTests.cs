using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Configs;

[TestFixture]
[TestOf(typeof(JsonBackedStep10PortfolioConfigProvider))]
public sealed class JsonBackedStep10PortfolioConfigProviderTests
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
        _configPath = Path.Combine(Path.GetTempPath(), $"config-10-portfolio-{Guid.NewGuid():N}.json");

        _paths = _fixture.Freeze<Mock<IPathsService>>();
        _paths.Setup(x => x.Config10PortfolioJson).Returns(_configPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private JsonBackedStep10PortfolioConfigProvider CreateSut()
        => _fixture.Create<JsonBackedStep10PortfolioConfigProvider>();

    [Test]
    public async Task GetConfigAsync_FilePresent_ReadsItsValues()
    {
        // Arrange
        await File.WriteAllTextAsync(_configPath, """
            {
              "_meta": { "config_version": "1.0.0" },
              "cash_policy": { "floor_pct": 0.08, "macro_override_enabled": true },
              "constraints": {
                "max_position_pct_of_portfolio": 0.12,
                "max_sector_pct_of_portfolio": 0.25,
                "min_trade_kr": 2500
              },
              "guards": { "skip_buy_below_conviction": 0.35, "skip_sell_below_conviction": 0.45 },
              "default_buy_target_pct": 0.07
            }
            """);

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Meta?.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(config.CashPolicy.FloorPct, Is.EqualTo(0.08m));
            Assert.That(config.CashPolicy.MacroOverrideEnabled, Is.True);
            Assert.That(config.Constraints.MaxPositionPctOfPortfolio, Is.EqualTo(0.12m));
            Assert.That(config.Constraints.MaxSectorPctOfPortfolio, Is.EqualTo(0.25m));
            Assert.That(config.Constraints.MinTradeKr, Is.EqualTo(2500m));
            Assert.That(config.Guards.SkipBuyBelowConviction, Is.EqualTo(0.35m));
            Assert.That(config.Guards.SkipSellBelowConviction, Is.EqualTo(0.45m));
            Assert.That(config.DefaultBuyTargetPct, Is.EqualTo(0.07m));
        });
    }

    [Test]
    public async Task GetConfigAsync_ContractFile_ReadsTheDefaultValues()
    {
        // Arrange — the contract's file carries "_doc" keys, a "_meta" block and an override
        // table keyed by regime names; every value must land on the property it names.
        _paths.Setup(x => x.Config10PortfolioJson).Returns(Paths.Config10PortfolioJsonAbs);
        var defaults = PortfolioConstructorConfig.Default;

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Meta?.ConfigVersion, Is.EqualTo(PortfolioConstructorConfig.ExpectedConfigVersion));
            Assert.That(config.CashPolicy.FloorPct, Is.EqualTo(defaults.CashPolicy.FloorPct));
            Assert.That(config.CashPolicy.MacroOverrideEnabled, Is.EqualTo(defaults.CashPolicy.MacroOverrideEnabled));
            Assert.That(config.CashPolicy.MacroOverrideTable, Is.EquivalentTo(defaults.CashPolicy.MacroOverrideTable));
            Assert.That(config.Constraints.MaxPositionPctOfPortfolio,
                Is.EqualTo(defaults.Constraints.MaxPositionPctOfPortfolio));
            Assert.That(config.Constraints.MaxSectorPctOfPortfolio,
                Is.EqualTo(defaults.Constraints.MaxSectorPctOfPortfolio));
            Assert.That(config.Constraints.MinTradeKr, Is.EqualTo(defaults.Constraints.MinTradeKr));
            Assert.That(config.Guards.SkipSellBelowConviction, Is.EqualTo(defaults.Guards.SkipSellBelowConviction));
            Assert.That(config.Guards.SkipSellBelowConvictionUnlessThesisInvalid,
                Is.EqualTo(defaults.Guards.SkipSellBelowConvictionUnlessThesisInvalid));
            Assert.That(config.Guards.SkipBuyBelowConviction, Is.EqualTo(defaults.Guards.SkipBuyBelowConviction));
            Assert.That(config.Sizing.DropBuyIfFallsBelowMinTrade,
                Is.EqualTo(defaults.Sizing.DropBuyIfFallsBelowMinTrade));
            Assert.That(config.RotationPairHandling.IfOneLegFails,
                Is.EqualTo(defaults.RotationPairHandling.IfOneLegFails));
            Assert.That(config.DefaultBuyTargetPct, Is.EqualTo(defaults.DefaultBuyTargetPct));
        });
    }

    [Test]
    public async Task GetConfigAsync_FileMissing_ReturnsDefaults()
    {
        // Arrange
        var defaults = PortfolioConstructorConfig.Default;

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Meta?.ConfigVersion, Is.EqualTo(PortfolioConstructorConfig.ExpectedConfigVersion));
            Assert.That(config.CashPolicy.FloorPct, Is.EqualTo(defaults.CashPolicy.FloorPct));
            Assert.That(config.Constraints.MinTradeKr, Is.EqualTo(defaults.Constraints.MinTradeKr));
        });
    }

    [Test]
    public async Task GetConfigAsync_WrongConfigVersion_ReturnsItUnchecked()
    {
        // Arrange — the version check stays in the agent, so a bad edit fails the run, not
        // the read.
        await File.WriteAllTextAsync(_configPath, """{ "_meta": { "config_version": "9.9.9" } }""");

        // Act
        var config = await CreateSut().GetConfigAsync();

        // Assert
        Assert.That(config.Meta?.ConfigVersion, Is.EqualTo("9.9.9"));
    }

    [Test]
    public async Task GetConfigAsync_FileEditedBetweenCalls_ReadsTheEdit()
    {
        // Arrange — the tab's config editor writes the same file, so an edit must show up
        // on the next run without a restart.
        var sut = CreateSut();
        await File.WriteAllTextAsync(_configPath, """{ "constraints": { "min_trade_kr": 1000 } }""");
        await sut.GetConfigAsync();
        await File.WriteAllTextAsync(_configPath, """{ "constraints": { "min_trade_kr": 3000 } }""");

        // Act
        var config = await sut.GetConfigAsync();

        // Assert
        Assert.That(config.Constraints.MinTradeKr, Is.EqualTo(3000m));
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
