using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Funds;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FikaFinans.Application.Tests.Pipeline.Steps;

[TestFixture]
[TestOf(typeof(Step10PortfolioReader))]
public sealed class Step10PortfolioReaderTests
{
    private IFixture _fixture = null!;
    private Mock<IHoldingsProvider> _holdings = null!;
    private Mock<IPortfolioStructureProvider> _structure = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _holdings = _fixture.Freeze<Mock<IHoldingsProvider>>();
        _structure = _fixture.Freeze<Mock<IPortfolioStructureProvider>>();

        SetupHoldings(0m);
        SetupStructure();
    }

    private Step10PortfolioReader CreateSut() => _fixture.Create<Step10PortfolioReader>();

    private void SetupHoldings(decimal cash, params Position[] positions) =>
        _holdings
            .Setup(x => x.GetHoldingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionsParseResult
            {
                Holdings = positions,
                CashAvailableKr = cash,
                Warnings = [],
                TotalRowCount = positions.Length + 1,
            });

    private void SetupStructure(params PinnedFund[] pins) =>
        _structure
            .Setup(x => x.GetStructureAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioStructure { PinnedFunds = pins });

    private static Position Held(string isin, decimal value, decimal costBasis = 0m, string? name = null) => new()
    {
        Isin = isin,
        Name = name ?? $"Test Fund {isin}",
        CurrentValueKr = value,
        CostBasisKr = costBasis,
    };

    [Test]
    public async Task ReadAsync_Holdings_CarriesTheirCash()
    {
        // Arrange
        SetupHoldings(42_000m);

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        Assert.That(portfolio.CashAvailableKr, Is.EqualTo(42_000m));
    }

    [Test]
    public async Task ReadAsync_HeldSinceTheRecord_TakesTheHoldingsValue()
    {
        // Arrange — the record says not held; a trade settled since says otherwise.
        SetupHoldings(0m, Held("LU0001", 12_000m, costBasis: 10_000m));

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        var fund = portfolio.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.CurrentlyHeld, Is.True);
            Assert.That(fund.CurrentValueKr, Is.EqualTo(12_000m));
            Assert.That(fund.CostBasisKr, Is.EqualTo(10_000m));
        });
    }

    [Test]
    public async Task ReadAsync_SoldSinceTheRecord_IsNotHeld()
    {
        // Arrange — the record says held; the holdings no longer have it.
        var record = MakeFund("LU0001", currentlyHeld: true, currentValueKr: 9_000m);

        // Act
        var portfolio = await CreateSut().ReadAsync([record]);

        // Assert
        var fund = portfolio.Funds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fund.CurrentlyHeld, Is.False);
            Assert.That(fund.CurrentValueKr, Is.Null);
            Assert.That(fund.CostBasisKr, Is.Null);
        });
    }

    [Test]
    public async Task ReadAsync_CorePinnedByIsin_IsCore()
    {
        // Arrange
        SetupStructure(new PinnedFund { Isin = "LU0001", Layer = PinnedLayer.Core });

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        Assert.That(portfolio.Funds.Single().Layer, Is.EqualTo(FundLayer.Core));
    }

    [Test]
    public async Task ReadAsync_CorePinnedByName_MatchesTheRecordsName()
    {
        // Arrange
        SetupStructure(new PinnedFund { Name = "Test Fund LU0001", Layer = PinnedLayer.Core });

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        Assert.That(portfolio.Funds.Single().Layer, Is.EqualTo(FundLayer.Core));
    }

    [Test]
    public async Task ReadAsync_PinRemovedSinceTheRecord_IsActive()
    {
        // Arrange — the record was written while the fund was pinned core.
        var record = MakeFund("LU0001", layer: FundLayer.Core);

        // Act
        var portfolio = await CreateSut().ReadAsync([record]);

        // Assert
        Assert.That(portfolio.Funds.Single().Layer, Is.EqualTo(FundLayer.Active));
    }

    [Test]
    public async Task ReadAsync_WriteoffHolding_IsFrozenNotAFund()
    {
        // Arrange — a writeoff fund has no record; only its holding says it is there.
        SetupStructure(new PinnedFund { Isin = "LU0WO", Layer = PinnedLayer.Writeoff });
        SetupHoldings(0m, Held("LU0WO", 182m, costBasis: 5_000m, name: "Old Fund"));

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        var frozen = portfolio.FrozenPositions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(frozen.Isin?.Value, Is.EqualTo("LU0WO"));
            Assert.That(frozen.Name, Is.EqualTo("Old Fund"));
            Assert.That(frozen.CurrentValueKr, Is.EqualTo(182m));
            Assert.That(frozen.CostBasisKr, Is.EqualTo(5_000m));
            Assert.That(portfolio.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU0001" }));
            Assert.That(portfolio.HeldWithoutRecord, Is.Empty);
        });
    }

    [Test]
    public async Task ReadAsync_WriteoffPinnedByName_MatchesTheHoldingsName()
    {
        // Arrange
        SetupStructure(new PinnedFund { Name = "Old Fund", Layer = PinnedLayer.Writeoff });
        SetupHoldings(0m, Held("LU0WO", 182m, name: "Old Fund"));

        // Act
        var portfolio = await CreateSut().ReadAsync([]);

        // Assert
        Assert.That(portfolio.FrozenPositions.Single().Isin?.Value, Is.EqualTo("LU0WO"));
    }

    [Test]
    public async Task ReadAsync_RecordPinnedWriteoff_IsLeftOutAndItsHoldingFrozen()
    {
        // Arrange — pinned by the record's name, which the holding does not share.
        SetupStructure(new PinnedFund { Name = "Test Fund LU0001", Layer = PinnedLayer.Writeoff });
        SetupHoldings(0m, Held("LU0001", 700m, name: "Bank's own name"));

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(portfolio.Funds, Is.Empty);
            Assert.That(portfolio.FrozenPositions.Single().Isin?.Value, Is.EqualTo("LU0001"));
            Assert.That(portfolio.HeldWithoutRecord, Is.Empty);
        });
    }

    [Test]
    public async Task ReadAsync_HeldWithNoRecord_IsReportedNotCounted()
    {
        // Arrange — a held fund whose runs keep failing has no step 9 record.
        SetupHoldings(0m, Held("LU0001", 5_000m), Held("LU0LOST", 8_000m));

        // Act
        var portfolio = await CreateSut().ReadAsync([MakeFund("LU0001")]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(portfolio.HeldWithoutRecord.Single().Isin.Value, Is.EqualTo("LU0LOST"));
            Assert.That(portfolio.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU0001" }));
            Assert.That(portfolio.FrozenPositions, Is.Empty);
        });
    }

    [Test]
    public async Task ReadAsync_Records_KeepsTheirOrder()
    {
        // Arrange
        FundRecord[] records = [MakeFund("LU0003"), MakeFund("LU0001"), MakeFund("LU0002")];

        // Act
        var portfolio = await CreateSut().ReadAsync(records);

        // Assert
        Assert.That(portfolio.Funds.Select(f => f.Isin.Value), Is.EqualTo(new[] { "LU0003", "LU0001", "LU0002" }));
    }

    [Test]
    public async Task ReadAsync_HoldingsAgreeWithTheRecord_CopiesEveryFieldThrough()
    {
        // Arrange — every field filled, the holdings and pins matching what the record says,
        // so the copy must serialize identically. Catches a FundRecord field the copy misses.
        // AutoFixture has no DateOnly of its own; the NAV buckets need one, taken from a fixed
        // clock so the record is the same on every run.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
        _fixture.Register(() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var record = _fixture.Build<FundRecord>()
            .With(f => f.Isin, "LU0001")
            .With(f => f.CurrentlyHeld, true)
            .With(f => f.CurrentValueKr, 12_000m)
            .With(f => f.CostBasisKr, 10_000m)
            .With(f => f.Layer, FundLayer.Active)
            .Create();
        SetupHoldings(0m, Held("LU0001", 12_000m, costBasis: 10_000m));

        // Act
        var portfolio = await CreateSut().ReadAsync([record]);

        // Assert
        Assert.That(JsonSerializer.Serialize(portfolio.Funds.Single()), Is.EqualTo(JsonSerializer.Serialize(record)));
    }

    [Test]
    public void ReadAsync_NullRecords_Throws()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => CreateSut().ReadAsync(null!));
    }

    private static FundRecord MakeFund(
        string isin,
        bool currentlyHeld = false,
        decimal? currentValueKr = null,
        FundLayer layer = FundLayer.Active) => new()
    {
        Isin = isin,
        Metadata = new FundMetadata
        {
            Isin = isin,
            Name = $"Test Fund {isin}",
            CompanyName = "TestCo",
            CurrencyCode = "SEK",
            Category = "Branschfond, Energi",
            FundType = "EQUITY_FUND",
            ManagedType = "ACTIVE",
            TotalFee = 1.0m,
            RecommendedHoldingPeriod = "FIVE_YEAR",
        },
        NavBuckets = Array.Empty<NavBucket>(),
        CurrentlyHeld = currentlyHeld,
        CurrentValueKr = currentValueKr,
        CostBasisKr = currentValueKr,
        Layer = layer,
    };
}
