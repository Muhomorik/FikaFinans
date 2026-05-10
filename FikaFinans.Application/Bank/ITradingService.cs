using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;
using FikaFinans.Domain.Bank.Trading;

namespace FikaFinans.Application.Bank;

public interface ITradingService
{
    Task<Result<TradingOrderId>> CreateBuyOrderAsync(FundId fundId, Money amount, CancellationToken ct = default);
    Task<Result<TradingOrderId>> CreateSellOrderAsync(FundId fundId, decimal units, CancellationToken ct = default);
    Task<Result> SettleOrderAsync(TradingOrderId orderId, decimal navPerUnit, CancellationToken ct = default);
    Task<IReadOnlyList<TradingOrder>> GetPendingOrdersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TradingOrder>> GetAllOrdersAsync(CancellationToken ct = default);
}
