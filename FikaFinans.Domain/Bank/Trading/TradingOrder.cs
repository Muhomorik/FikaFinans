using System.Diagnostics;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;

namespace FikaFinans.Domain.Bank.Trading;

[DebuggerDisplay("{Side} {FundId}: {Amount} / {Units} units ({Status})")]
public class TradingOrder
{
    public TradingOrderId Id { get; private init; }
    public FundId FundId { get; private init; }
    public OrderSide Side { get; private init; }
    public decimal AmountValue { get; private init; }
    public string Currency { get; private init; } = "SEK";
    public decimal? Units { get; private init; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? SettledAt { get; private set; }
    public decimal? SettlementNavPerUnit { get; private set; }
    public decimal? SettledUnits { get; private set; }

    private TradingOrder() { }

    public Money Amount => new(AmountValue, Currency);

    public static TradingOrder CreateBuyOrder(FundId fundId, Money amount, DateTimeOffset createdAt)
    {
        return new TradingOrder
        {
            Id = TradingOrderId.NewId(),
            FundId = fundId,
            Side = OrderSide.Buy,
            AmountValue = amount.Amount,
            Currency = amount.Currency,
            Units = null,
            Status = OrderStatus.Pending,
            CreatedAt = createdAt
        };
    }

    public static TradingOrder CreateSellOrder(FundId fundId, decimal units, DateTimeOffset createdAt, string currency = "SEK")
    {
        return new TradingOrder
        {
            Id = TradingOrderId.NewId(),
            FundId = fundId,
            Side = OrderSide.Sell,
            AmountValue = 0,
            Currency = currency,
            Units = units,
            Status = OrderStatus.Pending,
            CreatedAt = createdAt
        };
    }

    public Result Settle(decimal navPerUnit, DateTimeOffset settlementTime)
    {
        if (Status != OrderStatus.Pending)
            return Result.Fail("Order is not pending.");

        SettlementNavPerUnit = navPerUnit;
        SettledAt = settlementTime;
        Status = OrderStatus.Settled;
        SettledUnits = Side == OrderSide.Buy ? AmountValue / navPerUnit : Units;
        return Result.Ok();
    }

    public Result Cancel()
    {
        if (Status != OrderStatus.Pending)
            return Result.Fail("Order is not pending.");
        Status = OrderStatus.Cancelled;
        return Result.Ok();
    }

    // Storage rehydration: full state from a row, no factory branching.
    // Repos call this; domain code keeps using CreateBuyOrder/CreateSellOrder.
    public static TradingOrder Rehydrate(
        TradingOrderId id,
        FundId fundId,
        OrderSide side,
        decimal amountValue,
        string currency,
        decimal? units,
        OrderStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? settledAt,
        decimal? settlementNavPerUnit,
        decimal? settledUnits)
    {
        return new TradingOrder
        {
            Id = id,
            FundId = fundId,
            Side = side,
            AmountValue = amountValue,
            Currency = currency,
            Units = units,
            Status = status,
            CreatedAt = createdAt,
            SettledAt = settledAt,
            SettlementNavPerUnit = settlementNavPerUnit,
            SettledUnits = settledUnits
        };
    }
}
