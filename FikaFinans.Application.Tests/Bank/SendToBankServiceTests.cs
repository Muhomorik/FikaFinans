using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Bank.Events;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Portfolio;
using FluentResults;
using Moq;

namespace FikaFinans.Application.Tests.Bank;

[TestFixture]
[TestOf(typeof(SendToBankService))]
public sealed class SendToBankServiceTests
{
    private IFixture _fixture = null!;
    private Mock<ITradingService> _trading = null!;
    private Mock<IPortfolioQueryService> _portfolio = null!;
    private SendToBankService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _trading = _fixture.Freeze<Mock<ITradingService>>();
        _portfolio = _fixture.Freeze<Mock<IPortfolioQueryService>>();

        _trading
            .Setup(x => x.CreateBuyOrderAsync(It.IsAny<FundId>(), It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new TradingOrderId(Guid.NewGuid())));
        _trading
            .Setup(x => x.CreateSellOrderAsync(It.IsAny<FundId>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new TradingOrderId(Guid.NewGuid())));

        _sut = _fixture.Create<SendToBankService>();
    }

    [Test]
    public async Task SubmitAsync_BuyTrade_CreatesBuyOrderWithSekAmount()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 100m, currentValue: 10_000m) });
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.Buy, amountKr: 5_000m));

        var result = await _sut.SubmitAsync(trades);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(1));
            Assert.That(result.Skipped, Is.EqualTo(0));
        });
        _trading.Verify(x => x.CreateBuyOrderAsync(
            It.IsAny<FundId>(),
            It.Is<Money>(m => m.Amount == 5_000m && m.Currency == "SEK"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SubmitAsync_TopUpTrade_CreatesBuyOrder()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 100m, currentValue: 10_000m) });
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.TopUp, amountKr: 1_000m));

        var result = await _sut.SubmitAsync(trades);

        Assert.That(result.Sent, Is.EqualTo(1));
        _trading.Verify(x => x.CreateBuyOrderAsync(
            It.IsAny<FundId>(), It.IsAny<Money>(), It.IsAny<CancellationToken>()), Times.Once);
        _trading.Verify(x => x.CreateSellOrderAsync(
            It.IsAny<FundId>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SubmitAsync_SellTrade_SellsAllUnits()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 73.5m, currentValue: 7_350m) });
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.Sell, amountKr: 0m));

        var result = await _sut.SubmitAsync(trades);

        Assert.That(result.Sent, Is.EqualTo(1));
        _trading.Verify(x => x.CreateSellOrderAsync(
            It.IsAny<FundId>(), 73.5m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SubmitAsync_TrimTrade_DerivesUnitsFromNavPerUnit()
    {
        // 200 units, value 10_000 → NAV/unit = 50. Trim 2_500 kr → 50 units.
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 200m, currentValue: 10_000m) });
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.Trim, amountKr: 2_500m));

        var result = await _sut.SubmitAsync(trades);

        Assert.That(result.Sent, Is.EqualTo(1));
        _trading.Verify(x => x.CreateSellOrderAsync(
            It.IsAny<FundId>(), 50m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SubmitAsync_PartialSellTrade_DerivesUnitsFromNavPerUnit()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 200m, currentValue: 10_000m) });
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.PartialSell, amountKr: 1_500m));

        var result = await _sut.SubmitAsync(trades);

        Assert.That(result.Sent, Is.EqualTo(1));
        _trading.Verify(x => x.CreateSellOrderAsync(
            It.IsAny<FundId>(), 30m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SubmitAsync_TrimWithZeroUnits_SkipsWithoutCalling()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 0m, currentValue: 0m) });
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.Trim, amountKr: 1_000m));

        var result = await _sut.SubmitAsync(trades);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(0));
            Assert.That(result.Skipped, Is.EqualTo(1));
        });
        _trading.Verify(x => x.CreateSellOrderAsync(
            It.IsAny<FundId>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SubmitAsync_HoldAndNoOp_SilentlySkipped()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 100m, currentValue: 10_000m) });
        var trades = MakeTrades(
            MakeTrade("LU0001", TradeType.Hold, amountKr: 0m),
            MakeTrade("LU0001", TradeType.NoOp, amountKr: 0m));

        var result = await _sut.SubmitAsync(trades);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(0));
            Assert.That(result.Skipped, Is.EqualTo(0));
            Assert.That(result.Warnings, Is.Empty);
        });
        _trading.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SubmitAsync_TradeForMissingIsin_AddsWarningAndIncrementsSkipped()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FundPositionDto>());
        var trades = MakeTrades(MakeTrade("LU0999", TradeType.Buy, amountKr: 1_000m));

        var result = await _sut.SubmitAsync(trades);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(0));
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("LU0999"));
        });
        _trading.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SubmitAsync_TradingServiceRejects_AddsWarningAndIncrementsSkipped()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakePosition("LU0001", units: 100m, currentValue: 10_000m) });
        _trading
            .Setup(x => x.CreateBuyOrderAsync(It.IsAny<FundId>(), It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<TradingOrderId>("insufficient cash"));
        var trades = MakeTrades(MakeTrade("LU0001", TradeType.Buy, amountKr: 5_000m));

        var result = await _sut.SubmitAsync(trades);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(0));
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("LU0001").And.Contain("insufficient cash"));
        });
    }

    [Test]
    public async Task SubmitAsync_MixedTrades_TalliesSentAndSkippedSeparately()
    {
        _portfolio.Setup(x => x.GetFundPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakePosition("LU0001", units: 100m, currentValue: 10_000m),
                MakePosition("LU0002", units: 50m,  currentValue: 5_000m),
            });
        var trades = MakeTrades(
            MakeTrade("LU0001", TradeType.Buy, amountKr: 1_000m),
            MakeTrade("LU0002", TradeType.Sell, amountKr: 0m),
            MakeTrade("LU0999", TradeType.Buy, amountKr: 500m),  // missing -> skip
            MakeTrade("LU0001", TradeType.Hold, amountKr: 0m));  // skip silent

        var result = await _sut.SubmitAsync(trades);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(2));
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SubmitAsync_NullTrades_Throws()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _sut.SubmitAsync(null!));
    }

    private static TradesOutput MakeTrades(params Trade[] trades) => new()
    {
        GeneratedAt = "2026-05-26T00:00:00Z",
        IsoWeek = "2026-W21",
        ConfigVersion = "1.0.0",
        Trades = trades,
        RejectedRecommendations = Array.Empty<RejectedRecommendation>(),
        ConstraintViolations = Array.Empty<ConstraintViolation>(),
        CapitalSummary = new CapitalSummary
        {
            CashPolicy = new CashPolicySummary { FloorPct = 0.05m },
        },
    };

    private static Trade MakeTrade(string isin, TradeType type, decimal amountKr) => new()
    {
        Isin = isin,
        FundName = $"Fund {isin}",
        TradeType = type,
        TradeReason = "test",
        AmountKr = amountKr,
        SourceRecommendation = Recommendation.Maintain,
        SourceConviction = 0.5m,
        AuditNotes = Array.Empty<string>(),
    };

    private static FundPositionDto MakePosition(string isin, decimal units, decimal currentValue) => new(
        FundId: new FundId(Guid.NewGuid()),
        FundName: $"Fund {isin}",
        Isin: new Isin(isin),
        Units: units,
        CurrentValue: Money.SEK(currentValue),
        CostBasis: Money.SEK(currentValue),
        UnrealizedGainLoss: Money.SEK(0m),
        GainLossPercent: 0m);
}
