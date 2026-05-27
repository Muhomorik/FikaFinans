using FikaFinans.Application.Bank.Events;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Portfolio;
using FluentResults;
using NLog;

namespace FikaFinans.Application.Bank;

/// <summary>
/// Default <see cref="ISendToBankService"/>. Walks every trade in the supplied
/// <see cref="TradesOutput"/>, maps it to the matching bank-sim
/// <see cref="FundPositionDto"/> by ISIN, computes units for Trim /
/// PartialSell from the position's current NAV-per-unit, and submits the
/// order via <see cref="ITradingService"/>.
/// </summary>
public sealed class SendToBankService : ISendToBankService
{
    private readonly ILogger _logger;
    private readonly ITradingService _trading;
    private readonly IPortfolioQueryService _portfolio;

    public SendToBankService(ILogger logger, ITradingService trading, IPortfolioQueryService portfolio)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(trading);
        ArgumentNullException.ThrowIfNull(portfolio);
        _logger = logger;
        _trading = trading;
        _portfolio = portfolio;
    }

    public async Task<SendToBankResult> SubmitAsync(TradesOutput trades, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trades);

        var positions = await _portfolio.GetFundPositionsAsync(ct).ConfigureAwait(false);
        var isinMap = positions.ToDictionary(p => p.Isin);

        int sent = 0, skipped = 0;
        var warnings = new List<string>();

        foreach (var trade in trades.Trades)
        {
            if (trade.TradeType is TradeType.Hold or TradeType.NoOp)
                continue;

            if (!isinMap.TryGetValue(trade.Isin, out var pos))
            {
                var msg = $"No bank fund found for ISIN {trade.Isin} — skipping";
                _logger.Warn(msg);
                warnings.Add(msg);
                skipped++;
                continue;
            }

            Result<TradingOrderId> result;

            if (trade.TradeType is TradeType.Buy or TradeType.TopUp)
            {
                result = await _trading.CreateBuyOrderAsync(pos.FundId, Money.SEK(trade.AmountKr), ct)
                    .ConfigureAwait(false);
            }
            else if (trade.TradeType is TradeType.Sell)
            {
                result = await _trading.CreateSellOrderAsync(pos.FundId, pos.Units, ct).ConfigureAwait(false);
            }
            else // Trim, PartialSell
            {
                if (pos.Units <= 0) { skipped++; continue; }
                var navPerUnit = pos.CurrentValue.Amount / pos.Units;
                var units = navPerUnit > 0 ? trade.AmountKr / navPerUnit : 0m;
                if (units <= 0) { skipped++; continue; }
                result = await _trading.CreateSellOrderAsync(pos.FundId, units, ct).ConfigureAwait(false);
            }

            if (result.IsSuccess)
            {
                sent++;
            }
            else
            {
                var msg = $"Order rejected for {trade.Isin}: {result.Errors[0].Message}";
                _logger.Warn(msg);
                warnings.Add(msg);
                skipped++;
            }
        }

        return new SendToBankResult(sent, skipped, warnings);
    }
}
