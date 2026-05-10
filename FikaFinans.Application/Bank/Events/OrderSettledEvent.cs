using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Trading;

namespace FikaFinans.Application.Bank.Events;

public sealed record OrderSettledEvent(
    TradingOrderId OrderId,
    FundId FundId,
    OrderSide Side,
    decimal NavPerUnit,
    decimal SettledUnits,
    DateTimeOffset SettledAt);
